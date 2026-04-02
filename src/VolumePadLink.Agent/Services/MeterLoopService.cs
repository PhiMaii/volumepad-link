using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Services.Ring;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Services;

public sealed class MeterLoopService(
    IAudioService audioService,
    ISettingsService settingsService,
    RuntimeStateStore stateStore,
    EventBus eventBus,
    IRingRenderService ringRenderService,
    ILogger<MeterLoopService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var audio = await audioService.SampleMeterAsync(stoppingToken);
                var settings = settingsService.GetEffectiveSettings();
                var seq = stateStore.NextMeterSequence();
                var meterGain = double.IsFinite(settings.MeterGain)
                    ? Math.Clamp(settings.MeterGain, 0.10, 8.0)
                    : 1.0;
                var sourcePeak = double.IsFinite(audio.Peak) ? audio.Peak : 0.0;
                var sourceRms = double.IsFinite(audio.Rms) ? audio.Rms : 0.0;
                var amplifiedPeak = Math.Clamp(sourcePeak * meterGain, 0.0, 1.0);
                var amplifiedRms = Math.Clamp(sourceRms * meterGain, 0.0, 1.0);

                var meterTick = new MeterTick(
                    Seq: seq,
                    Peak: amplifiedPeak,
                    Rms: amplifiedRms,
                    Muted: audio.Muted,
                    Mode: settings.MeterMode,
                    Color: settings.MeterColor,
                    Brightness: settings.MeterBrightness,
                    Smoothing: settings.MeterSmoothing,
                    PeakHoldMs: settings.MeterPeakHoldMs,
                    MuteRedDurationMs: settings.MeterMuteRedDurationMs,
                    CapturedAtUtc: audio.CapturedAtUtc);

                await eventBus.PublishAsync(ProtocolNames.Events.AudioMeterTick, meterTick, stoppingToken);
                await ringRenderService.QueueMeterFrameAsync(meterTick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Meter loop iteration failed.");
            }
        }
    }
}
