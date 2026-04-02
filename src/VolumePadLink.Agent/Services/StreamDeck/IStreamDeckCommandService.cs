using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.StreamDeck;

public interface IStreamDeckCommandService
{
    Task<StreamDeckState> ToggleMuteAsync(CancellationToken cancellationToken);
    Task<StreamDeckState> AdjustVolumeByStepAsync(double step, CancellationToken cancellationToken);
}
