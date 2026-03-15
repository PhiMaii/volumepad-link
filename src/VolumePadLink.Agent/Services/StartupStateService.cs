using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.Services.Target;
using VolumePadLink.Agent.State;

namespace VolumePadLink.Agent.Services;

public sealed class StartupStateService(
    ISettingsStore settingsStore,
    TargetService targetService,
    AgentStateStore stateStore,
    IDeviceService deviceService,
    IAudioService audioService,
    ILogger<StartupStateService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var stored = await settingsStore.LoadAsync(cancellationToken);

        stateStore.SetSettings(stored.DeviceSettings);
        stateStore.SetAudioMode(stored.AudioMode);
        stateStore.SetDeviceStatus(stateStore.GetDeviceStatus() with { PortName = stored.PreferredDevicePort });

        await audioService.SetModeAsync(stored.AudioMode, cancellationToken);
        await targetService.SelectTargetAsync(stored.ActiveTarget, cancellationToken);

        if (!string.IsNullOrWhiteSpace(stored.PreferredDevicePort))
        {
            try
            {
                await deviceService.ConnectAsync(stored.PreferredDevicePort, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Automatic device reconnect on startup failed for {Port}.", stored.PreferredDevicePort);
            }
        }

        audioService.GraphChanged += OnGraphChangedAsync;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        audioService.GraphChanged -= OnGraphChangedAsync;
        return Task.CompletedTask;
    }

    private Task OnGraphChangedAsync(VolumePadLink.Contracts.DTOs.AudioGraphDto _)
    {
        return targetService.EnsureTargetAvailableAsync();
    }
}
