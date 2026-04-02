using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.StreamDeck;

public interface IStreamDeckStateProvider
{
    StreamDeckState GetStateSnapshot();
}
