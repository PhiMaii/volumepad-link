using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services;

public sealed class FixedInputService(
    IDeviceService deviceService,
    IAudioService audioService,
    ISettingsService settingsService,
    ILogger<FixedInputService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        deviceService.ButtonInputReceived += OnButtonInputReceived;
        deviceService.EncoderInputReceived += OnEncoderInputReceived;
        logger.LogInformation("Fixed hardware input mappings are active.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        deviceService.ButtonInputReceived -= OnButtonInputReceived;
        deviceService.EncoderInputReceived -= OnEncoderInputReceived;
        return Task.CompletedTask;
    }

    private void OnButtonInputReceived(object? sender, DeviceButtonInput input)
    {
        var action = FixedInputMapper.MapButton(input);
        if (action.Type != FixedInputActionType.ToggleMute)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await audioService.ToggleMuteAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed handling button input.");
            }
        });
    }

    private void OnEncoderInputReceived(object? sender, DeviceEncoderInput input)
    {
        var settings = settingsService.GetEffectiveSettings();
        var action = FixedInputMapper.MapEncoder(input, settings);
        if (action.Type != FixedInputActionType.VolumeDelta)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var current = await audioService.GetMasterAsync(CancellationToken.None);
                var target = Math.Clamp(current.Volume + action.Value, 0.0, 1.0);
                await audioService.SetVolumeAsync(target, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed handling encoder input.");
            }
        });
    }
}
