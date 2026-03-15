using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface ISettingsStore
{
    Task<StoredAgentSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(StoredAgentSettings settings, CancellationToken cancellationToken = default);
}

public sealed record StoredAgentSettings(
    ActiveTargetDto ActiveTarget,
    DeviceSettingsDto DeviceSettings,
    AudioMode AudioMode,
    string? PreferredDevicePort);
