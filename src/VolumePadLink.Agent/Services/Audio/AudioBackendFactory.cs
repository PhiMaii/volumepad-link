using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Audio;

public sealed class AudioBackendFactory(IServiceProvider serviceProvider) : IAudioBackendFactory
{
    public IAudioBackend Create(AudioMode mode)
    {
        return mode switch
        {
            AudioMode.Real => serviceProvider.GetRequiredService<CoreAudioBackend>(),
            _ => serviceProvider.GetRequiredService<SimulatedAudioBackend>()
        };
    }
}
