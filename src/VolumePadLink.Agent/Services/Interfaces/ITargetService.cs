using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface ITargetService
{
    Task<ActiveTargetDto> GetActiveTargetAsync(CancellationToken cancellationToken = default);

    Task<ActiveTargetDto> SelectTargetAsync(ActiveTargetDto target, CancellationToken cancellationToken = default);

    Task<float> ChangeActiveTargetVolumeAsync(float delta, CancellationToken cancellationToken = default);

    Task<bool> ToggleActiveTargetMuteAsync(CancellationToken cancellationToken = default);

    Task EnsureTargetAvailableAsync(CancellationToken cancellationToken = default);
}
