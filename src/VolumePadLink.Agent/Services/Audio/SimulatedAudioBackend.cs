using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.Audio;

public sealed class SimulatedAudioBackend : IAudioBackend
{
    private readonly object _gate = new();
    private readonly Random _random = new();

    private double _volume = 0.5;
    private bool _muted;
    private double _rms;

    public Task<AudioMasterState> GetMasterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(BuildSnapshotUnsafe());
        }
    }

    public Task<AudioMasterState> SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _volume = Math.Clamp(volume, 0.0, 1.0);
            return Task.FromResult(BuildSnapshotUnsafe());
        }
    }

    public Task<AudioMasterState> SetMuteAsync(bool muted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _muted = muted;
            return Task.FromResult(BuildSnapshotUnsafe());
        }
    }

    public Task<AudioMasterState> ToggleMuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _muted = !_muted;
            return Task.FromResult(BuildSnapshotUnsafe());
        }
    }

    public Task<AudioMasterState> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_muted)
            {
                _rms *= 0.85;
                return Task.FromResult(new AudioMasterState(_volume, true, 0.0, _rms, DateTimeOffset.UtcNow));
            }

            var basePeak = _volume * (0.45 + (_random.NextDouble() * 0.55));
            var peak = Math.Clamp(basePeak, 0.0, 1.0);
            _rms = (_rms * 0.75) + (peak * 0.25);
            return Task.FromResult(new AudioMasterState(_volume, false, peak, _rms, DateTimeOffset.UtcNow));
        }
    }

    public Task RestartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    private AudioMasterState BuildSnapshotUnsafe()
    {
        var peak = _muted ? 0.0 : _volume;
        return new AudioMasterState(_volume, _muted, peak, _rms, DateTimeOffset.UtcNow);
    }
}
