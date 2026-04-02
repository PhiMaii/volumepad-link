using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services;

public interface IAudioService
{
    Task<AudioMasterState> GetMasterAsync(CancellationToken cancellationToken);
    Task<AudioMasterState> SetVolumeAsync(double volume, CancellationToken cancellationToken);
    Task<AudioMasterState> SetMuteAsync(bool muted, CancellationToken cancellationToken);
    Task<AudioMasterState> ToggleMuteAsync(CancellationToken cancellationToken);
    Task<AudioMasterState> SampleMeterAsync(CancellationToken cancellationToken);
    Task RestartBackendAsync(CancellationToken cancellationToken);
}
