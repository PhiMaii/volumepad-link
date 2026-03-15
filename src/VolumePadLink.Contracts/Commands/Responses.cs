using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Contracts.Commands;

public sealed record AckResponse(bool Ok, string? Message = null);

public sealed record PingResponse(string Version, DateTimeOffset UtcNow);

public sealed record AudioGraphResponse(AudioGraphDto Graph);
public sealed record TargetResponse(ActiveTargetDto Target);
public sealed record DeviceStatusResponse(DeviceStatusDto Status);
public sealed record DeviceCapabilitiesResponse(DeviceCapabilitiesDto? Capabilities);
public sealed record SettingsResponse(DeviceSettingsDto Settings);
