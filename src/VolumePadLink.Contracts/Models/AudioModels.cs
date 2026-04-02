namespace VolumePadLink.Contracts.Models;

public sealed record AudioMasterState(
    double Volume,
    bool Muted,
    double Peak,
    double Rms,
    DateTimeOffset CapturedAtUtc);

public sealed record SetVolumeRequest(double Volume);

public sealed record SetMuteRequest(bool Muted);

public sealed record MeterTick(
    long Seq,
    double Peak,
    double Rms,
    bool Muted,
    string Mode,
    string Color,
    double Brightness,
    double Smoothing,
    int PeakHoldMs,
    int MuteRedDurationMs,
    DateTimeOffset CapturedAtUtc);
