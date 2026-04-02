using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services;

public interface ISettingsService
{
    Task InitializeAsync(CancellationToken cancellationToken);
    AppSettings GetEffectiveSettings();
    Task<SettingsUpdateResponse> ApplyAsync(AppSettings incomingSettings, CancellationToken cancellationToken);
}
