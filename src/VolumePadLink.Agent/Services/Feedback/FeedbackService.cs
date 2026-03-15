using VolumePadLink.Agent.Services.Interfaces;

namespace VolumePadLink.Agent.Services.Feedback;

public sealed class FeedbackService(
    IDeviceService deviceService,
    ILedService ledService,
    ITargetService targetService,
    ILogger<FeedbackService> logger) : BackgroundService
{
    private static readonly TimeSpan LedInterval = TimeSpan.FromMilliseconds(50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LedInterval, stoppingToken);

                await targetService.EnsureTargetAvailableAsync(stoppingToken);

                var status = await deviceService.GetStatusAsync(stoppingToken);
                if (!status.IsConnected)
                {
                    continue;
                }

                await ledService.PushFromCurrentStateAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "LED feedback iteration skipped due to transient error.");
            }
        }
    }
}
