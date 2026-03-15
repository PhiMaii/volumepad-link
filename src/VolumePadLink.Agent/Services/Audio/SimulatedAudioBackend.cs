using System.Diagnostics;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Audio;

public sealed class SimulatedAudioBackend : IAudioBackend
{
    private readonly Dictionary<string, (float Volume, bool Muted)> _sessionState = new(StringComparer.OrdinalIgnoreCase);
    private float _masterVolume = 0.5f;
    private bool _masterMuted;

    public AudioMode Mode => AudioMode.Simulated;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = CaptureSessionsSnapshot(now);
        var masterPeak = _masterMuted ? 0f : _masterVolume;

        var graph = new AudioGraphDto(
            new MasterAudioDto("default", "Default Output", _masterVolume, _masterMuted, masterPeak, masterPeak * 0.8f),
            sessions,
            now);

        return Task.FromResult(graph);
    }

    public Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default)
    {
        _masterVolume = Math.Clamp(value, 0f, 1f);
        return Task.CompletedTask;
    }

    public Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default)
    {
        _masterMuted = muted;
        return Task.CompletedTask;
    }

    public Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default)
    {
        var key = NormalizeSessionKey(sessionId);
        var state = _sessionState.TryGetValue(key, out var current) ? current : (Volume: 0.5f, Muted: false);
        _sessionState[key] = (Volume: Math.Clamp(value, 0f, 1f), Muted: state.Muted);
        return Task.CompletedTask;
    }

    public Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default)
    {
        var key = NormalizeSessionKey(sessionId);
        var state = _sessionState.TryGetValue(key, out var current) ? current : (Volume: 0.5f, Muted: false);
        _sessionState[key] = (Volume: state.Volume, Muted: muted);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<AudioSessionDto> CaptureSessionsSnapshot(DateTimeOffset now)
    {
        var processes = Process.GetProcesses()
            .Where(p => !string.IsNullOrWhiteSpace(p.ProcessName))
            .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

        var sessions = new List<AudioSessionDto>(processes.Length);

        foreach (var process in processes)
        {
            try
            {
                var sessionId = $"proc:{process.Id}";
                var key = NormalizeSessionKey(sessionId);
                if (!_sessionState.TryGetValue(key, out var state))
                {
                    state = (Volume: 0.5f, Muted: false);
                    _sessionState[key] = state;
                }

                var peak = state.Muted
                    ? 0f
                    : Math.Clamp(state.Volume * (0.65f + (float)(Math.Abs(Math.Sin(now.ToUnixTimeMilliseconds() / 750d)) * 0.35f)), 0f, 1f);

                sessions.Add(new AudioSessionDto(
                    sessionId,
                    process.Id,
                    process.ProcessName,
                    process.MainWindowTitle is { Length: > 0 } ? process.MainWindowTitle : process.ProcessName,
                    state.Volume,
                    state.Muted,
                    peak,
                    peak * 0.75f,
                    null,
                    true));
            }
            catch
            {
                // Process may disappear while enumerating.
            }
            finally
            {
                process.Dispose();
            }
        }

        return sessions;
    }

    private static string NormalizeSessionKey(string sessionId)
    {
        return sessionId.Trim();
    }
}
