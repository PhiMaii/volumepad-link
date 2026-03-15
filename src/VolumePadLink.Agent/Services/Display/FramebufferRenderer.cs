using System.Security.Cryptography;
using System.Text;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Display;

public sealed class FramebufferRenderer : IFramebufferRenderer
{
    public DisplayFrame Render(DisplayModelDto model)
    {
        const int width = 128;
        const int height = 64;

        var pixels = new byte[width * height];
        var seed = Encoding.UTF8.GetBytes($"{model.Screen}|{model.Title}|{model.Subtitle}|{model.ValueText}|{model.Muted}|{model.Accent}");
        var hash = SHA256.HashData(seed);

        for (var i = 0; i < pixels.Length; i++)
        {
            var hashByte = hash[i % hash.Length];
            var level = model.Muted ? 18 : 52;
            pixels[i] = (byte)Math.Clamp(level + hashByte % 160, 0, 255);
        }

        return new DisplayFrame(width, height, pixels);
    }
}
