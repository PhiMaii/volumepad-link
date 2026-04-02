namespace VolumePadLink.Contracts.Protocol;

public static class ProtocolNames
{
    public static class Methods
    {
        public const string DeviceListPorts = "device.listPorts";
        public const string DeviceConnect = "device.connect";
        public const string DeviceDisconnect = "device.disconnect";
        public const string DeviceReconnect = "device.reconnect";
        public const string DeviceGetStatus = "device.getStatus";

        public const string AudioMasterGet = "audio.master.get";
        public const string AudioMasterSetVolume = "audio.master.setVolume";
        public const string AudioMasterSetMute = "audio.master.setMute";
        public const string AudioMasterToggleMute = "audio.master.toggleMute";

        public const string SettingsGet = "settings.get";
        public const string SettingsUpdate = "settings.update";

        public const string DebugGetState = "debug.getState";
        public const string DebugApplyTuning = "debug.applyTuning";
        public const string DebugSetStream = "debug.setStream";

        public const string ServiceRestartAudioBackend = "service.restartAudioBackend";
    }

    public static class Events
    {
        public const string ConnectionStateChanged = "event.connection.stateChanged";
        public const string AudioMasterChanged = "event.audio.masterChanged";
        public const string AudioMeterTick = "event.audio.meterTick";
        public const string SettingsApplied = "event.settings.applied";
        public const string DebugState = "event.debug.state";
        public const string Diagnostics = "event.diagnostics";
    }

    public static class DeviceMethods
    {
        public const string DeviceHello = "device.hello";
        public const string DeviceApplySettings = "device.applySettings";
        public const string DeviceInputButton = "device.input.button";
        public const string DeviceInputEncoder = "device.input.encoder";
        public const string DeviceMeterFrame = "device.meter.frame";
        public const string DeviceRingSetLed = "device.ring.setLed";
        public const string DeviceRingStreamBegin = "device.ring.stream.begin";
        public const string DeviceRingStreamFrame = "device.ring.stream.frame";
        public const string DeviceRingStreamEnd = "device.ring.stream.end";
        public const string DeviceRingMuteOverride = "device.ring.muteOverride";
        public const string DeviceButtonLedsSet = "device.buttonLeds.set";
        public const string DebugGetState = "debug.getState";
        public const string DebugApplyTuning = "debug.applyTuning";
        public const string DebugSetStream = "debug.setStream";
        public const string DebugState = "debug.state";
    }

    public static class ErrorCodes
    {
        public const string UnknownMethod = "unknown_method";
        public const string InvalidPayload = "invalid_payload";
        public const string OutOfRange = "out_of_range";
        public const string NotConnected = "not_connected";
        public const string DeviceBusy = "device_busy";
        public const string Timeout = "timeout";
        public const string InternalError = "internal_error";
    }
}
