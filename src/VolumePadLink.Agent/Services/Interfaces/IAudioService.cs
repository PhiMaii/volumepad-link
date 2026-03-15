using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface IAudioService
{
    event Func<AudioGraphDto, Task>? GraphChanged;

    Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default);

    Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default);

    Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default);

    Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default);

    Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default);
}
