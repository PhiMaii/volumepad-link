using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Persistence;

public interface ISettingsStore
{
    Task<AppSettings?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
