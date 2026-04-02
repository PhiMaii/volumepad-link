using NAudio.CoreAudioApi;
using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.Audio;

public sealed class WasapiAudioBackend : IAudioBackend
{
    private readonly object _gate = new();
    private MMDeviceEnumerator _deviceEnumerator = new();
    private double _rms;

    public Task<AudioMasterState> GetMasterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ReadStateUnsafe());
        }
    }

    public Task<AudioMasterState> SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            using var device = GetOutputDeviceUnsafe();
            device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(volume, 0.0, 1.0);
            return Task.FromResult(ReadStateFromDeviceUnsafe(device));
        }
    }

    public Task<AudioMasterState> SetMuteAsync(bool muted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            using var device = GetOutputDeviceUnsafe();
            device.AudioEndpointVolume.Mute = muted;
            return Task.FromResult(ReadStateFromDeviceUnsafe(device));
        }
    }

    public Task<AudioMasterState> ToggleMuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            using var device = GetOutputDeviceUnsafe();
            device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
            return Task.FromResult(ReadStateFromDeviceUnsafe(device));
        }
    }

    public Task<AudioMasterState> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ReadStateUnsafe());
        }
    }

    public Task RestartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _deviceEnumerator.Dispose();
            _deviceEnumerator = new MMDeviceEnumerator();
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _deviceEnumerator.Dispose();
        }
    }

    private AudioMasterState ReadStateUnsafe()
    {
        using var device = GetOutputDeviceUnsafe();
        return ReadStateFromDeviceUnsafe(device);
    }

    private MMDevice GetOutputDeviceUnsafe()
    {
        return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private AudioMasterState ReadStateFromDeviceUnsafe(MMDevice device)
    {
        var volume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        var muted = device.AudioEndpointVolume.Mute;
        var peak = muted ? 0.0 : device.AudioMeterInformation.MasterPeakValue;
        _rms = (_rms * 0.7) + (peak * 0.3);

        return new AudioMasterState(volume, muted, peak, _rms, DateTimeOffset.UtcNow);
    }
}
