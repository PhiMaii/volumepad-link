namespace VolumePadLink.Agent.Services.Interfaces;

public interface IDirtyRegionTracker
{
    DisplayDiffResult? Diff(DisplayFrame? previous, DisplayFrame current);
}

public sealed record DisplayRect(int X, int Y, int Width, int Height, byte[] Pixels);

public sealed record DisplayDiffResult(bool IsFullFrame, DisplayRect? DirtyRect);
