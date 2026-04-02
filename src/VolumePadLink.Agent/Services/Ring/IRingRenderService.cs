using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.Ring;

public interface IRingRenderService
{
    ValueTask QueueMeterFrameAsync(MeterTick meterTick, CancellationToken cancellationToken);
    ValueTask BeginAnimationAsync(string streamId, string reason, CancellationToken cancellationToken);
    ValueTask QueueAnimationFrameAsync(DeviceRingStreamFrame frame, CancellationToken cancellationToken);
    ValueTask EndAnimationAsync(string streamId, CancellationToken cancellationToken);
    ValueTask SetLedAsync(DeviceRingSetLed led, CancellationToken cancellationToken);
    ValueTask TriggerMuteOverrideAsync(int durationMs, CancellationToken cancellationToken);
}
