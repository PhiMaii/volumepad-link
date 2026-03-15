using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface ILedService
{
    Task PushMeterAsync(LedMeterModelDto model, CancellationToken cancellationToken = default);

    Task PushFromCurrentStateAsync(CancellationToken cancellationToken = default);
}
