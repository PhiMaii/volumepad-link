namespace VolumePadLink.Agent.Options;

public sealed class AgentRuntimeOptions
{
    public const string SectionName = "Runtime";

    public string PipeName { get; set; } = "VolumePadLink.Agent.v2";
    public bool UseSimulatorDevice { get; set; } = true;
    public bool UseSimulatedAudioFallback { get; set; } = true;
    public string DataDirectory { get; set; } = string.Empty;
    public string SimulatorPortName { get; set; } = "SIMULATED";
    public string DefaultAutoConnectPort { get; set; } = "SIMULATED";
    public int DeviceRequestTimeoutMs { get; set; } = 1500;
    public int AutoReconnectDelayMs { get; set; } = 1200;

    public bool StreamDeckEndpointEnabled { get; set; } = true;
    public string StreamDeckBindAddress { get; set; } = "127.0.0.1";
    public int StreamDeckPort { get; set; } = 57691;
    public int StreamDeckTrafficCapacity { get; set; } = 2000;
    public int StreamDeckStatePollMs { get; set; } = 100;
}
