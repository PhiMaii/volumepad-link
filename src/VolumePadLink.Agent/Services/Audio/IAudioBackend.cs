using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.Audio;

public interface IAudioBackend : IDisposable
{
    Task<AudioMasterState> GetMasterAsync(CancellationToken cancellationToken);
    Task<AudioMasterState> SetVolumeAsync(double volume, CancellationToken cancellationToken);
    Task<AudioMasterState> SetMuteAsync(bool muted, CancellationToken cancellationToken);
    Task<AudioMasterState> ToggleMuteAsync(CancellationToken cancellationToken);
    Task<AudioMasterState> SampleAsync(CancellationToken cancellationToken);
    Task RestartAsync(CancellationToken cancellationToken);
}
