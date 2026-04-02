using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Persistence;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Services;

public sealed class SettingsService(
    ISettingsStore settingsStore,
    RuntimeStateStore stateStore,
    EventBus eventBus,
    IDeviceService deviceService,
    ILogger<SettingsService> logger) : ISettingsService
{
    private readonly object _gate = new();
    private AppSettings _effective = new();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AppSettings loaded = (await settingsStore.LoadAsync(cancellationToken)) ?? new AppSettings();
        var validation = SettingsValidator.ValidateAndNormalize(loaded);
        if (!validation.IsValid)
        {
            logger.LogWarning("Stored settings contain invalid values. Using normalized defaults.");
        }

        lock (_gate)
        {
            _effective = validation.Effective.Clone();
        }

        stateStore.UpdateSettings(validation.Effective);
    }

    public AppSettings GetEffectiveSettings()
    {
        lock (_gate)
        {
            return _effective.Clone();
        }
    }

    public async Task<SettingsUpdateResponse> ApplyAsync(AppSettings incomingSettings, CancellationToken cancellationToken)
    {
        var validation = SettingsValidator.ValidateAndNormalize(incomingSettings);
        if (!validation.IsValid)
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.OutOfRange, string.Join(" ", validation.Errors));
        }

        await settingsStore.SaveAsync(validation.Effective, cancellationToken);
        lock (_gate)
        {
            _effective = validation.Effective.Clone();
        }
        stateStore.UpdateSettings(validation.Effective);

        await eventBus.PublishAsync(
            ProtocolNames.Events.SettingsApplied,
            new SettingsAppliedEvent(validation.Effective.Clone()),
            cancellationToken);

        try
        {
            await deviceService.QueueCommandAsync(
                ProtocolNames.DeviceMethods.DeviceApplySettings,
                validation.Effective,
                DeviceCommandPriority.Medium,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed forwarding settings to device.");
        }

        return new SettingsUpdateResponse(validation.Effective.Clone());
    }
}
