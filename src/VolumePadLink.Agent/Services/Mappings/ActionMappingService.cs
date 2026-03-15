using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Mappings;

public sealed class ActionMappingService(
    IDeviceService deviceService,
    ITargetService targetService,
    IDisplayService displayService,
    ILogger<ActionMappingService> logger) : BackgroundService, IActionMappingService
{
    private const float VolumeStep = 0.02f;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        deviceService.InputEventReceived += OnInputEventAsync;

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            deviceService.InputEventReceived -= OnInputEventAsync;
        }
    }

    private async Task OnInputEventAsync(DeviceInputEventDto inputEvent)
    {
        try
        {
            if (inputEvent.ControlId.Contains("encoder", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(inputEvent.EventType, "rotate", StringComparison.OrdinalIgnoreCase) &&
                inputEvent.Delta != 0)
            {
                await targetService.ChangeActiveTargetVolumeAsync(inputEvent.Delta * VolumeStep);
                await displayService.PushTargetStatusAsync();
                return;
            }

            if (inputEvent.ControlId.Contains("button", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(inputEvent.EventType, "press", StringComparison.OrdinalIgnoreCase) &&
                inputEvent.Pressed)
            {
                await targetService.ToggleActiveTargetMuteAsync();
                await displayService.PushTargetStatusAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Action mapping execution failed for event {ControlId}/{EventType}", inputEvent.ControlId, inputEvent.EventType);
        }
    }
}
