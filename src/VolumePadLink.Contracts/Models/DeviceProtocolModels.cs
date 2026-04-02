namespace VolumePadLink.Contracts.Models;

public sealed class DeviceHello
{
    public string DeviceId { get; set; } = "volumepad-001";
    public string FirmwareVersion { get; set; } = "2.0.0";
    public int RingLedCount { get; set; } = 27;
    public int ButtonLedCount { get; set; } = 3;
}

public sealed class DeviceButtonInput
{
    public int ButtonId { get; set; }
    public string Action { get; set; } = "press";
    public DateTimeOffset TsUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DeviceEncoderInput
{
    public int DeltaSteps { get; set; }
    public bool Pressed { get; set; }
    public DateTimeOffset TsUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DeviceRingSetLed
{
    public int Index { get; set; }
    public string Color { get; set; } = "#00D26A";
    public double Brightness { get; set; } = 0.80;
}

public sealed class DeviceRingStreamBegin
{
    public string StreamId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int LedCount { get; set; } = 27;
}

public sealed class DeviceRingStreamFrame
{
    public string StreamId { get; set; } = string.Empty;
    public long Seq { get; set; }
    public IReadOnlyList<DeviceRingLedColor> Leds { get; set; } = [];
    public double Brightness { get; set; } = 0.80;
}

public sealed class DeviceRingLedColor
{
    public int Index { get; set; }
    public string Color { get; set; } = "#00D26A";
}

public sealed class DeviceRingStreamEnd
{
    public string StreamId { get; set; } = string.Empty;
}

public sealed class DeviceRingMuteOverride
{
    public string Color { get; set; } = "#FF0000";
    public int DurationMs { get; set; } = 700;
}

public sealed class DeviceButtonLedState
{
    public string Color { get; set; } = "#202020";
    public double Brightness { get; set; } = 0.2;
}

public sealed class DeviceButtonLedsSet
{
    public DeviceButtonLedState Button1 { get; set; } = new();
    public DeviceButtonLedState Button2 { get; set; } = new();
    public DeviceButtonLedState Button3 { get; set; } = new();
}
