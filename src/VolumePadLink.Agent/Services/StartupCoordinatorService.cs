using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Options;

namespace VolumePadLink.Agent.Services;

public sealed class StartupCoordinatorService(
    ISettingsService settingsService,
    IDeviceService deviceService,
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ILogger<StartupCoordinatorService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await settingsService.InitializeAsync(cancellationToken);

        var settings = settingsService.GetEffectiveSettings();
        if (!settings.AutoConnectOnStartup)
        {
            return;
        }

        try
        {
            var ports = await deviceService.ListPortsAsync(cancellationToken);
            var preferredPort = ports.Ports.FirstOrDefault(static port =>
                string.Equals(port.PortName, "SIMULATED", StringComparison.OrdinalIgnoreCase))
                ?? ports.Ports.FirstOrDefault();

            if (preferredPort is null)
            {
                logger.LogWarning("Auto-connect requested but no ports were discovered.");
                return;
            }

            var targetPort = runtimeOptions.Value.DefaultAutoConnectPort;
            if (!ports.Ports.Any(port => string.Equals(port.PortName, targetPort, StringComparison.OrdinalIgnoreCase)))
            {
                targetPort = preferredPort.PortName;
            }

            await deviceService.ConnectAsync(targetPort, cancellationToken);
            logger.LogInformation("Auto-connected to {Port} on startup.", targetPort);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto-connect on startup failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
