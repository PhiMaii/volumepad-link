using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Contracts.Commands;

public sealed record EmptyRequest;

public sealed record SetMasterVolumeRequest(float Value);
public sealed record SetMasterMuteRequest(bool Muted);
public sealed record SetSessionVolumeRequest(string SessionId, float Value);
public sealed record SetSessionMuteRequest(string SessionId, bool Muted);

public sealed record SelectTargetRequest(ActiveTargetDto Target);

public sealed record ConnectDeviceRequest(string? PortName);

public sealed record UpdateSettingsRequest(AppSettingsDto Settings);
