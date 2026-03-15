using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface IFramebufferRenderer
{
    DisplayFrame Render(DisplayModelDto model);
}

public sealed record DisplayFrame(int Width, int Height, byte[] Pixels);
