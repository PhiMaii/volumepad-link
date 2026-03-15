using System;

namespace VolumePadLink.UI.Models;

public sealed class AudioSessionItem
{
    public AudioSessionItem(string sessionId, string display, double volumePercent, bool muted)
    {
        SessionId = sessionId;
        Display = display;
        VolumePercent = volumePercent;
        Muted = muted;
    }

    public string SessionId { get; }

    public string Display { get; }

    public double VolumePercent { get; set; }

    public bool Muted { get; set; }

    public string MuteButtonText => Muted ? "Unmute" : "Mute";

    public override string ToString()
    {
        return $"{Display} ({Math.Round(VolumePercent)}%{(Muted ? ", muted" : string.Empty)})";
    }
}
