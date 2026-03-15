namespace VolumePadLink.Contracts.DTOs;

public enum AudioMode
{
    Real = 0,
    Simulated = 1
}

public sealed record AppSettingsDto(DeviceSettingsDto Device, AudioMode AudioMode);
