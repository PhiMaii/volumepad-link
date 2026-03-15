using VolumePadLink.Contracts.Abstractions;

namespace VolumePadLink.Agent.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string CommandPipeName { get; set; } = IpcDefaults.CommandPipeName;
    public string EventPipeName { get; set; } = IpcDefaults.EventPipeName;
    public int AudioPollIntervalMs { get; set; } = 1000;
    public int LedMeterIntervalMs { get; set; } = 50;
    public int DeviceReconnectIntervalMs { get; set; } = 1500;
    public int DeviceBaudRate { get; set; } = 115200;
    public string? PreferredDevicePort { get; set; }
    public string? SettingsPath { get; set; }
    public string? AudioModeOverride { get; set; }
}
