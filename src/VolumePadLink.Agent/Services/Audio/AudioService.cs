using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Audio;

public sealed class AudioService(
    AgentStateStore stateStore,
    IEventHub eventHub,
    IAudioBackendFactory backendFactory,
    IOptions<AgentOptions> options,
    ILogger<AudioService> logger) : BackgroundService, IAudioService
{
    private readonly SemaphoreSlim _backendGate = new(1, 1);

    private IAudioBackend? _backend;
    private AudioMode _backendMode = AudioMode.Real;

    public event Func<AudioGraphDto, Task>? GraphChanged;

    public Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(stateStore.GetAudioGraph());
    }

    public Task<AudioMode> GetModeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(stateStore.GetAudioMode());
    }

    public async Task<AudioMode> SetModeAsync(AudioMode mode, CancellationToken cancellationToken = default)
    {
        var effectiveMode = ResolveEffectiveMode(mode, out var overridden);

        if (overridden)
        {
            await eventHub.PublishAsync(
                EventNames.DiagnosticsWarning,
                new DiagnosticsEvent($"Audio mode override is active. Using {effectiveMode} instead of requested {mode}."),
                cancellationToken);
        }

        await SwitchBackendAsync(effectiveMode, cancellationToken);
        await PublishGraphFromBackendAsync(forcePublish: true, cancellationToken);

        return stateStore.GetAudioMode();
    }

    public async Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default)
    {
        await ExecuteBackendWriteAsync(async backend => await backend.SetMasterVolumeAsync(value, cancellationToken), cancellationToken);
        await PublishGraphFromBackendAsync(forcePublish: true, cancellationToken);
    }

    public async Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default)
    {
        await ExecuteBackendWriteAsync(async backend => await backend.SetMasterMuteAsync(muted, cancellationToken), cancellationToken);
        await PublishGraphFromBackendAsync(forcePublish: true, cancellationToken);
    }

    public async Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default)
    {
        await ExecuteBackendWriteAsync(async backend => await backend.SetSessionVolumeAsync(sessionId, value, cancellationToken), cancellationToken);
        await PublishGraphFromBackendAsync(forcePublish: true, cancellationToken);
    }

    public async Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default)
    {
        await ExecuteBackendWriteAsync(async backend => await backend.SetSessionMuteAsync(sessionId, muted, cancellationToken), cancellationToken);
        await PublishGraphFromBackendAsync(forcePublish: true, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Audio service started.");

        var startupRequestedMode = stateStore.GetAudioMode();
        var startupMode = ResolveEffectiveMode(startupRequestedMode, out var overriddenAtStartup);

        if (overriddenAtStartup)
        {
            logger.LogInformation("Audio mode override active. Startup requested {Requested}, effective {Effective}.", startupRequestedMode, startupMode);
        }

        await SwitchBackendAsync(startupMode, stoppingToken);
        await PublishGraphFromBackendAsync(forcePublish: true, stoppingToken);

        var interval = TimeSpan.FromMilliseconds(Math.Max(200, options.Value.AudioPollIntervalMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);

                var desiredMode = ResolveEffectiveMode(stateStore.GetAudioMode(), out _);
                if (desiredMode != stateStore.GetAudioMode())
                {
                    stateStore.SetAudioMode(desiredMode);
                }

                await SwitchBackendAsync(desiredMode, stoppingToken);
                await PublishGraphFromBackendAsync(forcePublish: false, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Audio polling iteration failed.");
            }
        }

        await DisposeCurrentBackendAsync();
        logger.LogInformation("Audio service stopped.");
    }

    private async Task SwitchBackendAsync(AudioMode desiredMode, CancellationToken cancellationToken)
    {
        await _backendGate.WaitAsync(cancellationToken);
        try
        {
            if (_backend is not null && _backendMode == desiredMode)
            {
                stateStore.SetAudioMode(desiredMode);
                return;
            }

            if (_backend is not null)
            {
                await _backend.DisposeAsync();
                _backend = null;
            }

            try
            {
                _backend = backendFactory.Create(desiredMode);
                await _backend.InitializeAsync(cancellationToken);
                _backendMode = desiredMode;
                stateStore.SetAudioMode(desiredMode);

                await eventHub.PublishAsync(
                    EventNames.DiagnosticsWarning,
                    new DiagnosticsEvent($"Audio backend switched to {desiredMode}."),
                    cancellationToken);
            }
            catch (Exception ex) when (desiredMode == AudioMode.Real)
            {
                logger.LogWarning(ex, "Real Core Audio backend failed to initialize. Falling back to simulated audio backend.");

                _backend = backendFactory.Create(AudioMode.Simulated);
                await _backend.InitializeAsync(cancellationToken);
                _backendMode = AudioMode.Simulated;
                stateStore.SetAudioMode(AudioMode.Simulated);

                await eventHub.PublishAsync(
                    EventNames.DiagnosticsError,
                    new DiagnosticsEvent("Real Core Audio backend initialization failed. Falling back to simulated audio.", ex.Message),
                    cancellationToken);
            }
        }
        finally
        {
            _backendGate.Release();
        }
    }

    private async Task ExecuteBackendWriteAsync(Func<IAudioBackend, Task> operation, CancellationToken cancellationToken)
    {
        await _backendGate.WaitAsync(cancellationToken);
        try
        {
            if (_backend is null)
            {
                var mode = ResolveEffectiveMode(stateStore.GetAudioMode(), out _);
                _backend = backendFactory.Create(mode);
                await _backend.InitializeAsync(cancellationToken);
                _backendMode = mode;
                stateStore.SetAudioMode(mode);
            }

            await operation(_backend);
        }
        finally
        {
            _backendGate.Release();
        }
    }

    private async Task PublishGraphFromBackendAsync(bool forcePublish, CancellationToken cancellationToken)
    {
        AudioGraphDto graph;

        await _backendGate.WaitAsync(cancellationToken);
        try
        {
            if (_backend is null)
            {
                return;
            }

            graph = await _backend.GetGraphAsync(cancellationToken);
        }
        finally
        {
            _backendGate.Release();
        }

        var previous = stateStore.GetAudioGraph();
        stateStore.SetAudioGraph(graph);

        var graphChanged = forcePublish || !EquivalentForEvents(previous, graph);
        if (!graphChanged)
        {
            return;
        }

        if (GraphChanged is { } graphHandlers)
        {
            foreach (Func<AudioGraphDto, Task> handler in graphHandlers.GetInvocationList())
            {
                await handler(graph);
            }
        }

        await eventHub.PublishAsync(EventNames.AudioGraphChanged, new AudioGraphChangedEvent(graph), cancellationToken);
        await eventHub.PublishAsync(EventNames.AudioMasterChanged, new AudioMasterChangedEvent(graph.Master), cancellationToken);

        var previousById = previous.Sessions.ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);
        var currentById = graph.Sessions.ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);

        foreach (var current in graph.Sessions)
        {
            if (!previousById.TryGetValue(current.SessionId, out var old))
            {
                await eventHub.PublishAsync(EventNames.AudioSessionAdded, new AudioSessionChangedEvent(current), cancellationToken);
                continue;
            }

            if (!EquivalentSessionForEvents(old, current))
            {
                await eventHub.PublishAsync(EventNames.AudioSessionUpdated, new AudioSessionChangedEvent(current), cancellationToken);
            }
        }

        foreach (var old in previous.Sessions)
        {
            if (!currentById.ContainsKey(old.SessionId))
            {
                await eventHub.PublishAsync(EventNames.AudioSessionRemoved, new AudioSessionRemovedEvent(old.SessionId), cancellationToken);
            }
        }
    }

    private static bool EquivalentForEvents(AudioGraphDto left, AudioGraphDto right)
    {
        if (left.Master.Volume != right.Master.Volume ||
            left.Master.Muted != right.Master.Muted ||
            left.Sessions.Count != right.Sessions.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Sessions.Count; i++)
        {
            if (!EquivalentSessionForEvents(left.Sessions[i], right.Sessions[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EquivalentSessionForEvents(AudioSessionDto left, AudioSessionDto right)
    {
        return left.SessionId == right.SessionId && left.Volume == right.Volume && left.Muted == right.Muted;
    }

    private AudioMode ResolveEffectiveMode(AudioMode requested, out bool overridden)
    {
        overridden = false;

        if (string.IsNullOrWhiteSpace(options.Value.AudioModeOverride))
        {
            return requested;
        }

        if (!Enum.TryParse<AudioMode>(options.Value.AudioModeOverride, ignoreCase: true, out var overrideMode))
        {
            return requested;
        }

        overridden = overrideMode != requested;
        return overrideMode;
    }

    private async Task DisposeCurrentBackendAsync()
    {
        await _backendGate.WaitAsync();
        try
        {
            if (_backend is not null)
            {
                await _backend.DisposeAsync();
                _backend = null;
            }
        }
        finally
        {
            _backendGate.Release();
        }
    }
}
