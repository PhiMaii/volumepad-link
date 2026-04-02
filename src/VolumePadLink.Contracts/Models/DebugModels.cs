namespace VolumePadLink.Contracts.Models;

public sealed class DebugTuning
{
    public double DetentStrengthMaxVPerRad { get; set; } = 2.0;
    public double SnapStrengthMaxVPerRad { get; set; } = 2.0;
    public double ClickPulseVoltage { get; set; } = 1.2;
    public int ClickPulseMs { get; set; } = 34;
    public double EndstopMinPos { get; set; } = -1.0;
    public double EndstopMaxPos { get; set; } = 1.0;
    public double EndstopMinStrength { get; set; } = 0.7;
    public double EndstopMaxStrength { get; set; } = 0.7;
}

public sealed class DebugState
{
    public string DeviceId { get; set; } = "volumepad-001";
    public string Source { get; set; } = "firmware";
    public long UptimeMs { get; set; }
    public bool HapticsReady { get; set; } = true;
    public int Position { get; set; } = 42;
    public int DetentCount { get; set; } = 24;
    public double DetentStrength { get; set; } = 0.65;
    public double SnapStrength { get; set; } = 0.40;
}

public sealed record DebugApplyTuningRequest(DebugTuning Tuning);

public sealed record DebugSetStreamRequest(bool Enabled, int IntervalMs);

public sealed record DebugStateEvent(DebugState State);
