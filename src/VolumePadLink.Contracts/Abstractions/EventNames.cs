namespace VolumePadLink.Contracts.Abstractions;

public static class EventNames
{
    public const string AudioGraphChanged = "Audio.GraphChanged";
    public const string AudioMasterChanged = "Audio.MasterChanged";
    public const string AudioSessionAdded = "Audio.SessionAdded";
    public const string AudioSessionUpdated = "Audio.SessionUpdated";
    public const string AudioSessionRemoved = "Audio.SessionRemoved";

    public const string TargetActiveChanged = "Target.ActiveChanged";

    public const string DeviceConnected = "Device.Connected";
    public const string DeviceDisconnected = "Device.Disconnected";
    public const string DeviceInputEvent = "Device.InputEvent";
    public const string DeviceCapabilitiesReceived = "Device.CapabilitiesReceived";
    public const string DeviceSettingsApplied = "Device.SettingsApplied";

    public const string DiagnosticsWarning = "Diagnostics.Warning";
    public const string DiagnosticsError = "Diagnostics.Error";
}
