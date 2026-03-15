using VolumePadLink.Agent.Services.Interfaces;

namespace VolumePadLink.Agent.Services.Display;

public sealed class DirtyRegionTracker : IDirtyRegionTracker
{
    public DisplayDiffResult? Diff(DisplayFrame? previous, DisplayFrame current)
    {
        if (previous is null || previous.Width != current.Width || previous.Height != current.Height || previous.Pixels.Length != current.Pixels.Length)
        {
            return new DisplayDiffResult(true, null);
        }

        var width = current.Width;
        var total = current.Pixels.Length;

        var minX = width;
        var minY = current.Height;
        var maxX = -1;
        var maxY = -1;
        var changed = 0;

        for (var i = 0; i < total; i++)
        {
            if (previous.Pixels[i] == current.Pixels[i])
            {
                continue;
            }

            changed++;
            var x = i % width;
            var y = i / width;

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (changed == 0)
        {
            return null;
        }

        var changeRatio = (double)changed / total;
        if (changeRatio > 0.35d)
        {
            return new DisplayDiffResult(true, null);
        }

        var rectWidth = maxX - minX + 1;
        var rectHeight = maxY - minY + 1;
        var rectPixels = new byte[rectWidth * rectHeight];

        for (var row = 0; row < rectHeight; row++)
        {
            var sourceOffset = (minY + row) * width + minX;
            Array.Copy(current.Pixels, sourceOffset, rectPixels, row * rectWidth, rectWidth);
        }

        var rect = new DisplayRect(minX, minY, rectWidth, rectHeight, rectPixels);
        return new DisplayDiffResult(false, rect);
    }
}
