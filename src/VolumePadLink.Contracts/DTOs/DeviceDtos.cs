namespace VolumePadLink.Contracts.DTOs;

public sealed record DeviceStatusDto(
    bool IsConnected,
    string? PortName,
    string? DeviceId,
    string? FirmwareVersion,
    DateTimeOffset? LastSeenUtc,
    DeviceCapabilitiesDto? Capabilities);

public sealed record DeviceCapabilitiesDto(
    string ProtocolVersion,
    bool SupportsDisplay,
    bool SupportsLedMeter,
    int MaxPacketSize,
    int MaxFrameRateHz);

public sealed record DeviceInputEventDto(
    string DeviceId,
    string ControlId,
    string EventType,
    int Delta,
    bool Pressed,
    int Position,
    DateTimeOffset TimestampUtc);

public sealed record DeviceSettingsDto(
    int DetentCount,
    float DetentStrength,
    float SnapStrength,
    float LedBrightness,
    float DisplayBrightness,
    bool EncoderInvert,
    int ButtonLongPressMs);
