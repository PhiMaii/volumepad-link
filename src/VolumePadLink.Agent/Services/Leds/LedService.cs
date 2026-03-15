using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Leds;

public sealed class LedService(
    AgentStateStore stateStore,
    IAudioService audioService,
    ITargetService targetService,
    IDeviceService deviceService) : ILedService
{
    public Task PushMeterAsync(LedMeterModelDto model, CancellationToken cancellationToken = default)
    {
        return deviceService.SendLedMeterAsync(model, cancellationToken);
    }

    public async Task PushFromCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        var graph = await audioService.GetGraphAsync(cancellationToken);
        var target = await targetService.GetActiveTargetAsync(cancellationToken);
        var settings = stateStore.GetSettings();

        float rms;
        float peak;
        bool muted;

        if (target.Kind == TargetKinds.SessionById && !string.IsNullOrWhiteSpace(target.SessionId))
        {
            var session = graph.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, target.SessionId, StringComparison.OrdinalIgnoreCase));
            if (session is null)
            {
                rms = graph.Master.Rms;
                peak = graph.Master.Peak;
                muted = graph.Master.Muted;
            }
            else
            {
                rms = session.Rms;
                peak = session.Peak;
                muted = session.Muted;
            }
        }
        else
        {
            rms = graph.Master.Rms;
            peak = graph.Master.Peak;
            muted = graph.Master.Muted;
        }

        var model = new LedMeterModelDto("peak-rms", rms, peak, muted, "audio-default", settings.LedBrightness);
        await deviceService.SendLedMeterAsync(model, cancellationToken);
    }
}
