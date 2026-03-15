namespace VolumePadLink.Contracts.Abstractions;

public static class CommandNames
{
    public const string AppPing = "App.Ping";

    public const string AudioGetGraph = "Audio.GetGraph";
    public const string AudioSetMasterVolume = "Audio.SetMasterVolume";
    public const string AudioSetMasterMute = "Audio.SetMasterMute";
    public const string AudioSetSessionVolume = "Audio.SetSessionVolume";
    public const string AudioSetSessionMute = "Audio.SetSessionMute";

    public const string TargetGetActive = "Target.GetActive";
    public const string TargetSelect = "Target.Select";

    public const string DeviceGetStatus = "Device.GetStatus";
    public const string DeviceConnect = "Device.Connect";
    public const string DeviceDisconnect = "Device.Disconnect";
    public const string DeviceGetCapabilities = "Device.GetCapabilities";

    public const string SettingsGet = "Settings.Get";
    public const string SettingsUpdate = "Settings.Update";
}
