using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Contracts.Commands;

public sealed record AudioGraphChangedEvent(AudioGraphDto Graph);
public sealed record AudioMasterChangedEvent(MasterAudioDto Master);
public sealed record AudioSessionChangedEvent(AudioSessionDto Session);
public sealed record AudioSessionRemovedEvent(string SessionId);

public sealed record TargetActiveChangedEvent(ActiveTargetDto Target);

public sealed record DeviceConnectedEvent(DeviceStatusDto Status);
public sealed record DeviceDisconnectedEvent(string? Reason);
public sealed record DeviceCapabilitiesReceivedEvent(DeviceCapabilitiesDto Capabilities);
public sealed record DeviceSettingsAppliedEvent(DeviceSettingsDto EffectiveSettings);
public sealed record DiagnosticsEvent(string Message, string? Details = null);
