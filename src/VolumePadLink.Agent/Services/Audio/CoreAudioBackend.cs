using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Audio;

public sealed class CoreAudioBackend(ILogger<CoreAudioBackend> logger) : IAudioBackend
{
    private readonly object _sync = new();
    private readonly Dictionary<string, float> _sessionRms = new(StringComparer.OrdinalIgnoreCase);

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioSessionManager? _sessionManager;
    private DateTimeOffset _lastEndpointCheckUtc = DateTimeOffset.MinValue;
    private float _masterRms;

    public AudioMode Mode => AudioMode.Real;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EnsureDeviceInitializedLocked();
        }

        return Task.CompletedTask;
    }

    public Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EnsureDeviceInitializedLocked();
            EnsureDefaultEndpointCurrentLocked();
            return Task.FromResult(CaptureGraphLocked());
        }
    }

    public Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EnsureDeviceInitializedLocked();
            EnsureDefaultEndpointCurrentLocked();
            _device!.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f);
        }

        return Task.CompletedTask;
    }

    public Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EnsureDeviceInitializedLocked();
            EnsureDefaultEndpointCurrentLocked();
            _device!.AudioEndpointVolume.Mute = muted;
        }

        return Task.CompletedTask;
    }

    public Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EnsureDeviceInitializedLocked();
            EnsureDefaultEndpointCurrentLocked();

            using var target = FindSessionLocked(sessionId);
            if (target is null)
            {
                logger.LogDebug("CoreAudio session {SessionId} not found for volume update.", sessionId);
                return Task.CompletedTask;
            }

            target.SimpleAudioVolume.Volume = Math.Clamp(value, 0f, 1f);
        }

        return Task.CompletedTask;
    }

    public Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EnsureDeviceInitializedLocked();
            EnsureDefaultEndpointCurrentLocked();

            using var target = FindSessionLocked(sessionId);
            if (target is null)
            {
                logger.LogDebug("CoreAudio session {SessionId} not found for mute update.", sessionId);
                return Task.CompletedTask;
            }

            target.SimpleAudioVolume.Mute = muted;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            TearDownLocked();
        }

        return ValueTask.CompletedTask;
    }

    private void EnsureDeviceInitializedLocked()
    {
        if (_device is not null && _sessionManager is not null)
        {
            return;
        }

        _enumerator ??= new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _sessionManager = _device.AudioSessionManager;
        _lastEndpointCheckUtc = DateTimeOffset.UtcNow;

        _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
        _sessionManager.OnSessionCreated += OnSessionCreated;
    }

    private void EnsureDefaultEndpointCurrentLocked()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastEndpointCheckUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastEndpointCheckUtc = now;

        using var probe = new MMDeviceEnumerator();
        var currentDefault = probe.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        if (_device is not null && string.Equals(_device.ID, currentDefault.ID, StringComparison.Ordinal))
        {
            currentDefault.Dispose();
            return;
        }

        TearDownLocked();

        _enumerator ??= new MMDeviceEnumerator();
        _device = currentDefault;
        _sessionManager = _device.AudioSessionManager;

        _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
        _sessionManager.OnSessionCreated += OnSessionCreated;
    }

    private AudioGraphDto CaptureGraphLocked()
    {
        var device = _device!;
        var sessionManager = _sessionManager!;
        var endpointId = device.ID;

        var masterVolume = Safe(() => device.AudioEndpointVolume.MasterVolumeLevelScalar, 0.5f);
        var masterMuted = Safe(() => device.AudioEndpointVolume.Mute, false);
        var masterPeak = Safe(() => device.AudioMeterInformation.MasterPeakValue, 0f);
        _masterRms = (_masterRms * 0.82f) + (masterPeak * 0.18f);

        var sessions = new List<AudioSessionDto>();
        var activeSessionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sessionManager.Sessions.Count; i++)
        {
            AudioSessionControl? session = null;

            try
            {
                session = sessionManager.Sessions[i];
                var processId = (int)Safe(() => session.GetProcessID, 0u);
                var instanceId = Safe(() => session.GetSessionInstanceIdentifier, string.Empty) ?? string.Empty;
                var stableSessionId = BuildStableSessionId(endpointId, instanceId, processId);

                var volume = Safe(() => session.SimpleAudioVolume.Volume, 0f);
                var muted = Safe(() => session.SimpleAudioVolume.Mute, false);
                var peak = Safe(() => session.AudioMeterInformation.MasterPeakValue, 0f);
                var rms = (_sessionRms.TryGetValue(stableSessionId, out var previousRms) ? previousRms : peak) * 0.82f + (peak * 0.18f);
                _sessionRms[stableSessionId] = rms;
                activeSessionKeys.Add(stableSessionId);

                var processName = ResolveProcessName(processId);
                var displayName = ResolveDisplayName(session, processId, processName);
                var isActive = session.State != AudioSessionState.AudioSessionStateExpired;

                sessions.Add(new AudioSessionDto(
                    stableSessionId,
                    processId,
                    processName,
                    displayName,
                    volume,
                    muted,
                    peak,
                    rms,
                    null,
                    isActive));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skipping unreadable CoreAudio session at index {Index}.", i);
            }
            finally
            {
                session?.Dispose();
            }
        }

        PruneMissingSessionRms(activeSessionKeys);

        return new AudioGraphDto(
            new MasterAudioDto(endpointId, device.FriendlyName, masterVolume, masterMuted, masterPeak, _masterRms),
            sessions,
            DateTimeOffset.UtcNow);
    }

    private AudioSessionControl? FindSessionLocked(string sessionId)
    {
        var endpointId = _device!.ID;
        var byPid = TryExtractLegacyPid(sessionId, out var pidFromLegacy) ? pidFromLegacy : -1;

        for (var i = 0; i < _sessionManager!.Sessions.Count; i++)
        {
            AudioSessionControl? session = null;

            try
            {
                session = _sessionManager.Sessions[i];
                var processId = (int)Safe(() => session.GetProcessID, 0u);

                if (byPid > -1 && processId == byPid)
                {
                    return session;
                }

                var instanceId = Safe(() => session.GetSessionInstanceIdentifier, string.Empty) ?? string.Empty;
                var stableSessionId = BuildStableSessionId(endpointId, instanceId, processId);
                if (string.Equals(stableSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skipping session during lookup for {SessionId}.", sessionId);
            }

            session?.Dispose();
        }

        return null;
    }

    private static string BuildStableSessionId(string endpointId, string sessionInstanceId, int processId)
    {
        return $"{endpointId}|{sessionInstanceId}|{processId}";
    }

    private static bool TryExtractLegacyPid(string sessionId, out int processId)
    {
        processId = -1;
        if (!sessionId.StartsWith("proc:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(sessionId.AsSpan(5), out processId);
    }

    private static string ResolveProcessName(int processId)
    {
        if (processId <= 0)
        {
            return "System";
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return $"pid-{processId}";
        }
    }

    private static string ResolveDisplayName(AudioSessionControl session, int processId, string processName)
    {
        var sessionName = Safe(() => session.DisplayName, string.Empty);
        if (!string.IsNullOrWhiteSpace(sessionName))
        {
            return sessionName;
        }

        if (processId <= 0)
        {
            return processName;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
            {
                return process.MainWindowTitle;
            }
        }
        catch
        {
            // Ignore process lookup errors and keep processName fallback.
        }

        return processName;
    }

    private void PruneMissingSessionRms(HashSet<string> activeKeys)
    {
        var toRemove = _sessionRms.Keys.Where(key => !activeKeys.Contains(key)).ToArray();
        foreach (var key in toRemove)
        {
            _sessionRms.Remove(key);
        }
    }

    private void TearDownLocked()
    {
        if (_device is not null)
        {
            try
            {
                _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
            }
            catch
            {
                // Ignore callback detach failures.
            }

            if (_sessionManager is not null)
            {
                try
                {
                    _sessionManager.OnSessionCreated -= OnSessionCreated;
                }
                catch
                {
                    // Ignore callback detach failures.
                }
            }

            _device.Dispose();
            _device = null;
            _sessionManager = null;
        }

        _enumerator?.Dispose();
        _enumerator = null;
        _sessionRms.Clear();
        _masterRms = 0f;
        _lastEndpointCheckUtc = DateTimeOffset.MinValue;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData _)
    {
        _lastEndpointCheckUtc = DateTimeOffset.MinValue;
    }

    private void OnSessionCreated(object? sender, IAudioSessionControl _)
    {
        _lastEndpointCheckUtc = DateTimeOffset.MinValue;
    }

    private static T Safe<T>(Func<T> producer, T fallback)
    {
        try
        {
            return producer();
        }
        catch
        {
            return fallback;
        }
    }
}
