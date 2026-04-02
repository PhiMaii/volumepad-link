using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Core;

public sealed class RuntimeStateStore
{
    private readonly object _gate = new();

    private DeviceStatus _deviceStatus = new(
        ConnectionState.Disconnected,
        PortName: null,
        DeviceId: null,
        FirmwareVersion: null,
        LastSeenUtc: null);

    private AudioMasterState _audioMaster = new(
        Volume: 0.5,
        Muted: false,
        Peak: 0.0,
        Rms: 0.0,
        CapturedAtUtc: DateTimeOffset.UtcNow);

    private AppSettings _settings = new();
    private DebugState _debugState = new();
    private bool _debugStreamEnabled;
    private int _debugStreamIntervalMs = 150;
    private long _meterSequence;

    public AgentRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return BuildSnapshotUnsafe();
        }
    }

    public AgentRuntimeSnapshot UpdateDeviceStatus(Func<DeviceStatus, DeviceStatus> update)
    {
        lock (_gate)
        {
            _deviceStatus = update(_deviceStatus);
            return BuildSnapshotUnsafe();
        }
    }

    public AgentRuntimeSnapshot UpdateAudioMaster(Func<AudioMasterState, AudioMasterState> update)
    {
        lock (_gate)
        {
            _audioMaster = update(_audioMaster);
            return BuildSnapshotUnsafe();
        }
    }

    public AgentRuntimeSnapshot UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _settings = settings.Clone();
            return BuildSnapshotUnsafe();
        }
    }

    public AgentRuntimeSnapshot UpdateDebugState(DebugState state)
    {
        lock (_gate)
        {
            _debugState = new DebugState
            {
                DeviceId = state.DeviceId,
                Source = state.Source,
                UptimeMs = state.UptimeMs,
                HapticsReady = state.HapticsReady,
                Position = state.Position,
                DetentCount = state.DetentCount,
                DetentStrength = state.DetentStrength,
                SnapStrength = state.SnapStrength,
            };
            return BuildSnapshotUnsafe();
        }
    }

    public AgentRuntimeSnapshot UpdateDebugStream(bool enabled, int intervalMs)
    {
        lock (_gate)
        {
            _debugStreamEnabled = enabled;
            _debugStreamIntervalMs = intervalMs;
            return BuildSnapshotUnsafe();
        }
    }

    public long NextMeterSequence()
    {
        lock (_gate)
        {
            _meterSequence++;
            return _meterSequence;
        }
    }

    private AgentRuntimeSnapshot BuildSnapshotUnsafe()
    {
        return new AgentRuntimeSnapshot
        {
            DeviceStatus = _deviceStatus with { },
            AudioMaster = _audioMaster with { },
            Settings = _settings.Clone(),
            DebugState = new DebugState
            {
                DeviceId = _debugState.DeviceId,
                Source = _debugState.Source,
                UptimeMs = _debugState.UptimeMs,
                HapticsReady = _debugState.HapticsReady,
                Position = _debugState.Position,
                DetentCount = _debugState.DetentCount,
                DetentStrength = _debugState.DetentStrength,
                SnapStrength = _debugState.SnapStrength,
            },
            DebugStreamEnabled = _debugStreamEnabled,
            DebugStreamIntervalMs = _debugStreamIntervalMs,
            MeterSequence = _meterSequence,
        };
    }
}
