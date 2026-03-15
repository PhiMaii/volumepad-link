using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Target;

public sealed class TargetService(
    AgentStateStore stateStore,
    IAudioService audioService,
    ISettingsStore settingsStore,
    IEventHub eventHub,
    ILogger<TargetService> logger) : ITargetService
{
    public Task<ActiveTargetDto> GetActiveTargetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(stateStore.GetActiveTarget());
    }

    public async Task<ActiveTargetDto> SelectTargetAsync(ActiveTargetDto target, CancellationToken cancellationToken = default)
    {
        var normalized = await NormalizeTargetAsync(target, cancellationToken);
        stateStore.SetActiveTarget(normalized);

        await PersistAsync(cancellationToken);
        await eventHub.PublishAsync(EventNames.TargetActiveChanged, new TargetActiveChangedEvent(normalized), cancellationToken);

        return normalized;
    }

    public async Task<float> ChangeActiveTargetVolumeAsync(float delta, CancellationToken cancellationToken = default)
    {
        var target = await NormalizeTargetAsync(stateStore.GetActiveTarget(), cancellationToken);

        if (target.Kind == TargetKinds.Master)
        {
            var graph = await audioService.GetGraphAsync(cancellationToken);
            var next = Math.Clamp(graph.Master.Volume + delta, 0f, 1f);
            await audioService.SetMasterVolumeAsync(next, cancellationToken);
            return next;
        }

        var sessionId = target.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            await SelectTargetAsync(new ActiveTargetDto(TargetKinds.Master, null, null), cancellationToken);
            var graph = await audioService.GetGraphAsync(cancellationToken);
            return graph.Master.Volume;
        }

        var currentGraph = await audioService.GetGraphAsync(cancellationToken);
        var session = currentGraph.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            logger.LogInformation("Active target session {SessionId} disappeared. Falling back to master.", sessionId);
            await SelectTargetAsync(new ActiveTargetDto(TargetKinds.Master, null, null), cancellationToken);
            return (await audioService.GetGraphAsync(cancellationToken)).Master.Volume;
        }

        var nextVolume = Math.Clamp(session.Volume + delta, 0f, 1f);
        await audioService.SetSessionVolumeAsync(session.SessionId, nextVolume, cancellationToken);
        return nextVolume;
    }

    public async Task<bool> ToggleActiveTargetMuteAsync(CancellationToken cancellationToken = default)
    {
        var target = await NormalizeTargetAsync(stateStore.GetActiveTarget(), cancellationToken);

        if (target.Kind == TargetKinds.Master)
        {
            var graph = await audioService.GetGraphAsync(cancellationToken);
            var nextMute = !graph.Master.Muted;
            await audioService.SetMasterMuteAsync(nextMute, cancellationToken);
            return nextMute;
        }

        var sessionId = target.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            await SelectTargetAsync(new ActiveTargetDto(TargetKinds.Master, null, null), cancellationToken);
            var graph = await audioService.GetGraphAsync(cancellationToken);
            return graph.Master.Muted;
        }

        var currentGraph = await audioService.GetGraphAsync(cancellationToken);
        var session = currentGraph.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            await SelectTargetAsync(new ActiveTargetDto(TargetKinds.Master, null, null), cancellationToken);
            var graph = await audioService.GetGraphAsync(cancellationToken);
            return graph.Master.Muted;
        }

        var nextMuted = !session.Muted;
        await audioService.SetSessionMuteAsync(session.SessionId, nextMuted, cancellationToken);
        return nextMuted;
    }

    public async Task EnsureTargetAvailableAsync(CancellationToken cancellationToken = default)
    {
        var current = stateStore.GetActiveTarget();
        var normalized = await NormalizeTargetAsync(current, cancellationToken);
        if (normalized == current)
        {
            return;
        }

        await SelectTargetAsync(normalized, cancellationToken);
    }

    public async Task LoadPersistedStateAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settingsStore.LoadAsync(cancellationToken);
        stateStore.SetSettings(stored.DeviceSettings);

        var normalized = await NormalizeTargetAsync(stored.ActiveTarget, cancellationToken);
        stateStore.SetActiveTarget(normalized);
    }

    private async Task<ActiveTargetDto> NormalizeTargetAsync(ActiveTargetDto target, CancellationToken cancellationToken)
    {
        if (target.Kind == TargetKinds.Master)
        {
            return new ActiveTargetDto(TargetKinds.Master, null, null);
        }

        if (target.Kind != TargetKinds.SessionById)
        {
            return new ActiveTargetDto(TargetKinds.Master, null, null);
        }

        if (string.IsNullOrWhiteSpace(target.SessionId))
        {
            return new ActiveTargetDto(TargetKinds.Master, null, null);
        }

        var graph = await audioService.GetGraphAsync(cancellationToken);
        var exists = graph.Sessions.Any(s => string.Equals(s.SessionId, target.SessionId, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            return new ActiveTargetDto(TargetKinds.Master, null, null);
        }

        return new ActiveTargetDto(TargetKinds.SessionById, target.SessionId, null);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var settings = stateStore.GetSettings();
        var target = stateStore.GetActiveTarget();
        var status = stateStore.GetDeviceStatus();
        await settingsStore.SaveAsync(new StoredAgentSettings(target, settings, stateStore.GetAudioMode(), status.PortName), cancellationToken);
    }
}

