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
