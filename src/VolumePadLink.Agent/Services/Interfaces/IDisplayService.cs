using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface IDisplayService
{
    Task PushTargetStatusAsync(CancellationToken cancellationToken = default);

    Task PushModelAsync(DisplayModelDto model, CancellationToken cancellationToken = default);
}
