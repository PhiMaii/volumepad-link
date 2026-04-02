using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services;

public interface IDebugService
{
    Task<DebugState> GetStateAsync(CancellationToken cancellationToken);
    Task<DebugState> ApplyTuningAsync(DebugTuning tuning, CancellationToken cancellationToken);
    Task<DebugState> SetStreamAsync(bool enabled, int intervalMs, CancellationToken cancellationToken);
}
