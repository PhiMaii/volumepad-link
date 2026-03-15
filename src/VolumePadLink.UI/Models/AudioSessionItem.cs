using System;

namespace VolumePadLink.UI.Models;

public sealed record AudioSessionItem(string SessionId, string Display, float Volume, bool Muted)
{
    public override string ToString()
    {
        return $"{Display} ({Math.Round(Volume * 100)}%{(Muted ? ", muted" : string.Empty)})";
    }
}
