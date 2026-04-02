namespace VolumePadLink.Contracts.Models;

public sealed class StreamDeckState
{
    public StreamDeckMasterState Master { get; set; } = new();
    public StreamDeckDeviceConnection DeviceConnection { get; set; } = new();
    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StreamDeckMasterState
{
    public double Volume { get; set; }
    public bool Muted { get; set; }
}

public sealed class StreamDeckDeviceConnection
{
    public ConnectionState State { get; set; } = ConnectionState.Disconnected;
    public string? PortName { get; set; }
}

public sealed class StreamDeckVolumeStepRequest
{
    public double Step { get; set; } = 0.02;
}

public sealed class StreamDeckSettingsPatch
{
    public bool? AutoReconnectOnError { get; set; }
    public bool? AutoConnectOnStartup { get; set; }

    public double? VolumeStepSize { get; set; }
    public int? DetentCount { get; set; }
    public double? DetentStrength { get; set; }
    public double? SnapStrength { get; set; }
    public bool? EncoderInvert { get; set; }
    public double? LedBrightness { get; set; }

    public string? MeterMode { get; set; }
    public string? MeterColor { get; set; }
    public double? MeterBrightness { get; set; }
    public double? MeterGain { get; set; }
    public double? MeterSmoothing { get; set; }
    public int? MeterPeakHoldMs { get; set; }
    public int? MeterMuteRedDurationMs { get; set; }

    public bool? LowEndstopEnabled { get; set; }
    public double? LowEndstopPosition { get; set; }
    public double? LowEndstopStrength { get; set; }

    public bool? HighEndstopEnabled { get; set; }
    public double? HighEndstopPosition { get; set; }
    public double? HighEndstopStrength { get; set; }
}

public sealed class StreamDeckSettingsSnapshot
{
    public AppSettings Effective { get; set; } = new();
    public StreamDeckState State { get; set; } = new();
}

public sealed class StreamDeckStateEventPayload
{
    public StreamDeckState State { get; set; } = new();
}

public sealed class StreamDeckEventEnvelope
{
    public string Type { get; set; } = string.Empty;
    public object Payload { get; set; } = new();
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
