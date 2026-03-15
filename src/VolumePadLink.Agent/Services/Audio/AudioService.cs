using System.Diagnostics;
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
    ILogger<AudioService> logger,
    Microsoft.Extensions.Options.IOptions<AgentOptions> options) : BackgroundService, IAudioService
{
    private readonly Dictionary<string, (float Volume, bool Muted)> _sessionState = new(StringComparer.OrdinalIgnoreCase);
    private float _masterVolume = 0.5f;
    private bool _masterMuted;

    public event Func<AudioGraphDto, Task>? GraphChanged;

    public Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(stateStore.GetAudioGraph());
    }

    public async Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default)
    {
        _masterVolume = Clamp01(value);
        await PublishGraphAsync(cancellationToken);
    }

    public async Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default)
    {
        _masterMuted = muted;
        await PublishGraphAsync(cancellationToken);
    }

    public async Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default)
    {
        var key = NormalizeSessionKey(sessionId);
        var state = _sessionState.TryGetValue(key, out var current) ? current : (Volume: 0.5f, Muted: false);
        _sessionState[key] = (Volume: Clamp01(value), Muted: state.Muted);
        await PublishGraphAsync(cancellationToken);
    }

    public async Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default)
    {
        var key = NormalizeSessionKey(sessionId);
        var state = _sessionState.TryGetValue(key, out var current) ? current : (Volume: 0.5f, Muted: false);
        _sessionState[key] = (Volume: state.Volume, Muted: muted);
        await PublishGraphAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Audio service started.");

        await PublishGraphAsync(stoppingToken);
        var interval = TimeSpan.FromMilliseconds(Math.Max(250, options.Value.AudioPollIntervalMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await PublishGraphAsync(stoppingToken);
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

        logger.LogInformation("Audio service stopped.");
    }

    private async Task PublishGraphAsync(CancellationToken cancellationToken)
    {
        var previousGraph = stateStore.GetAudioGraph();
        var sessions = CaptureSessionsSnapshot();
        var masterPeak = _masterMuted ? 0f : _masterVolume;

        var graph = new AudioGraphDto(
            new MasterAudioDto(
                "default",
                "Default Output",
                _masterVolume,
                _masterMuted,
                masterPeak,
                masterPeak * 0.8f),
            sessions,
            DateTimeOffset.UtcNow);

        stateStore.SetAudioGraph(graph);

        var graphChanged = !AudioGraphEquals(previousGraph, graph);
        if (!graphChanged)
        {
            return;
        }

        if (GraphChanged is { } handlers)
        {
            foreach (Func<AudioGraphDto, Task> handler in handlers.GetInvocationList())
            {
                await handler(graph);
            }
        }

        await eventHub.PublishAsync(EventNames.AudioGraphChanged, new AudioGraphChangedEvent(graph), cancellationToken);
        await eventHub.PublishAsync(EventNames.AudioMasterChanged, new AudioMasterChangedEvent(graph.Master), cancellationToken);

        var previousById = previousGraph.Sessions.ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);
        var currentById = graph.Sessions.ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);

        foreach (var current in graph.Sessions)
        {
            if (!previousById.TryGetValue(current.SessionId, out var previous))
            {
                await eventHub.PublishAsync(EventNames.AudioSessionAdded, new AudioSessionChangedEvent(current), cancellationToken);
                continue;
            }

            if (!SessionEquals(previous, current))
            {
                await eventHub.PublishAsync(EventNames.AudioSessionUpdated, new AudioSessionChangedEvent(current), cancellationToken);
            }
        }

        foreach (var previous in previousGraph.Sessions)
        {
            if (!currentById.ContainsKey(previous.SessionId))
            {
                await eventHub.PublishAsync(EventNames.AudioSessionRemoved, new AudioSessionRemovedEvent(previous.SessionId), cancellationToken);
            }
        }
    }

    private IReadOnlyList<AudioSessionDto> CaptureSessionsSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
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

                var peak = state.Muted ? 0f : Clamp01(state.Volume * (0.65f + (float)(Math.Abs(Math.Sin(now.ToUnixTimeMilliseconds() / 750d)) * 0.35f)));
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
                // Process may have exited while enumerating.
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

    private static bool AudioGraphEquals(AudioGraphDto left, AudioGraphDto right)
    {
        if (left.Master.Volume != right.Master.Volume ||
            left.Master.Muted != right.Master.Muted ||
            left.Sessions.Count != right.Sessions.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Sessions.Count; i++)
        {
            if (!SessionEquals(left.Sessions[i], right.Sessions[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SessionEquals(AudioSessionDto left, AudioSessionDto right)
    {
        return left.SessionId == right.SessionId &&
               left.Volume == right.Volume &&
               left.Muted == right.Muted;
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }
}
