namespace VolumePadLink.Contracts.Models;

public sealed record DevicePortInfo(string PortName, bool IsBusy);

public sealed record DeviceListPortsResponse(IReadOnlyList<DevicePortInfo> Ports);

public sealed record DeviceConnectRequest(string PortName);

public sealed record DeviceStatus(
    ConnectionState ConnectionState,
    string? PortName,
    string? DeviceId,
    string? FirmwareVersion,
    DateTimeOffset? LastSeenUtc);

public sealed record ConnectionStateChangedEvent(
    ConnectionState ConnectionState,
    string? PortName,
    string? DeviceId,
    string? FirmwareVersion,
    DateTimeOffset? LastSeenUtc,
    string? Reason = null);
