using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.State;

public sealed class AgentStateStore
{
    private readonly object _sync = new();

    private AudioGraphDto _audioGraph = new(
        new MasterAudioDto("default", "Default Output", 0.5f, false, 0f, 0f),
        [],
        DateTimeOffset.UtcNow);

    private ActiveTargetDto _activeTarget = new(TargetKinds.Master, null, null);
    private MeterSourceDto _meterSource = new(MeterSourceKinds.ActiveTarget, null, null);
    private DeviceStatusDto _deviceStatus = new(false, null, null, null, null, null);
    private DeviceSettingsDto _settings = new(24, 0.65f, 0.4f, 0.8f, 0.8f, false, 450);
    private string _activeProfile = "Default";

    public AudioGraphDto GetAudioGraph()
    {
        lock (_sync)
        {
            return _audioGraph;
        }
    }

    public void SetAudioGraph(AudioGraphDto graph)
    {
        lock (_sync)
        {
            _audioGraph = graph;
        }
    }

    public ActiveTargetDto GetActiveTarget()
    {
        lock (_sync)
        {
            return _activeTarget;
        }
    }

    public void SetActiveTarget(ActiveTargetDto target)
    {
        lock (_sync)
        {
            _activeTarget = target;
        }
    }

    public MeterSourceDto GetMeterSource()
    {
        lock (_sync)
        {
            return _meterSource;
        }
    }

    public void SetMeterSource(MeterSourceDto source)
    {
        lock (_sync)
        {
            _meterSource = source;
        }
    }

    public DeviceStatusDto GetDeviceStatus()
    {
        lock (_sync)
        {
            return _deviceStatus;
        }
    }

    public void SetDeviceStatus(DeviceStatusDto status)
    {
        lock (_sync)
        {
            _deviceStatus = status;
        }
    }

    public DeviceSettingsDto GetSettings()
    {
        lock (_sync)
        {
            return _settings;
        }
    }

    public void SetSettings(DeviceSettingsDto settings)
    {
        lock (_sync)
        {
            _settings = settings;
        }
    }

    public string GetActiveProfile()
    {
        lock (_sync)
        {
            return _activeProfile;
        }
    }

    public void SetActiveProfile(string profile)
    {
        lock (_sync)
        {
            _activeProfile = profile;
        }
    }
}
