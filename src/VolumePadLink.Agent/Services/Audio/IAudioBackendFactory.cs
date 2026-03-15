using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Audio;

public interface IAudioBackendFactory
{
    IAudioBackend Create(AudioMode mode);
}
