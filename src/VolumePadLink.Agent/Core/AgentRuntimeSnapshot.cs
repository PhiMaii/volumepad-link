using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Core;

public sealed class AgentRuntimeSnapshot
{
    public required DeviceStatus DeviceStatus { get; init; }
    public required AudioMasterState AudioMaster { get; init; }
    public required AppSettings Settings { get; init; }
    public required DebugState DebugState { get; init; }
    public bool DebugStreamEnabled { get; init; }
    public int DebugStreamIntervalMs { get; init; }
    public long MeterSequence { get; init; }
}
