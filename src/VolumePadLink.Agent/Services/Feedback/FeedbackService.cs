using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.Services.Interfaces;

namespace VolumePadLink.Agent.Services.Feedback;

public sealed class FeedbackService(
    IOptions<AgentOptions> options,
    IDeviceService deviceService,
    ILedService ledService,
    IDisplayService displayService,
    ITargetService targetService,
    ILogger<FeedbackService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(25, options.Value.LedMeterIntervalMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);

                await targetService.EnsureTargetAvailableAsync(stoppingToken);

                var status = await deviceService.GetStatusAsync(stoppingToken);
                if (!status.IsConnected)
                {
                    continue;
                }

                await ledService.PushFromCurrentStateAsync(stoppingToken);
                await displayService.PushTargetStatusAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Feedback iteration skipped due to transient error.");
            }
        }
    }
}
