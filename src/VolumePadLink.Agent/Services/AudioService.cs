using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Services.Audio;
using VolumePadLink.Agent.Services.Ring;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Services;

public sealed class AudioService(
    IAudioBackend audioBackend,
    RuntimeStateStore stateStore,
    EventBus eventBus,
    ISettingsService settingsService,
    IRingRenderService ringRenderService,
    ILogger<AudioService> logger) : IAudioService
{
    public async Task<AudioMasterState> GetMasterAsync(CancellationToken cancellationToken)
    {
        var state = await audioBackend.GetMasterAsync(cancellationToken);
        stateStore.UpdateAudioMaster(_ => state);
        return state;
    }

    public async Task<AudioMasterState> SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        var state = await audioBackend.SetVolumeAsync(volume, cancellationToken);
        stateStore.UpdateAudioMaster(_ => state);
        await PublishMasterChangedAsync(state, cancellationToken);
        return state;
    }

    public async Task<AudioMasterState> SetMuteAsync(bool muted, CancellationToken cancellationToken)
    {
        var state = await audioBackend.SetMuteAsync(muted, cancellationToken);
        stateStore.UpdateAudioMaster(_ => state);
        await PublishMasterChangedAsync(state, cancellationToken);

        if (muted)
        {
            var settings = settingsService.GetEffectiveSettings();
            await ringRenderService.TriggerMuteOverrideAsync(settings.MeterMuteRedDurationMs, cancellationToken);
        }

        return state;
    }

    public async Task<AudioMasterState> ToggleMuteAsync(CancellationToken cancellationToken)
    {
        var state = await audioBackend.ToggleMuteAsync(cancellationToken);
        stateStore.UpdateAudioMaster(_ => state);
        await PublishMasterChangedAsync(state, cancellationToken);

        if (state.Muted)
        {
            var settings = settingsService.GetEffectiveSettings();
            await ringRenderService.TriggerMuteOverrideAsync(settings.MeterMuteRedDurationMs, cancellationToken);
        }

        return state;
    }

    public async Task<AudioMasterState> SampleMeterAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await audioBackend.SampleAsync(cancellationToken);
            stateStore.UpdateAudioMaster(_ => state);
            return state;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audio sample failed.");
            return stateStore.GetSnapshot().AudioMaster;
        }
    }

    public async Task RestartBackendAsync(CancellationToken cancellationToken)
    {
        await audioBackend.RestartAsync(cancellationToken);
        var state = await audioBackend.GetMasterAsync(cancellationToken);
        stateStore.UpdateAudioMaster(_ => state);
        await PublishMasterChangedAsync(state, cancellationToken);
    }

    private ValueTask PublishMasterChangedAsync(AudioMasterState state, CancellationToken cancellationToken)
    {
        return eventBus.PublishAsync(ProtocolNames.Events.AudioMasterChanged, state, cancellationToken);
    }
}
