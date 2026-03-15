using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Audio;

public interface IAudioBackend : IAsyncDisposable
{
    AudioMode Mode { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default);

    Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default);

    Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default);

    Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default);

    Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default);
}
