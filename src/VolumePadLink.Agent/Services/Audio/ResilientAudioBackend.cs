using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.Audio;

public sealed class ResilientAudioBackend(
    ILogger<ResilientAudioBackend> logger,
    WasapiAudioBackend primary,
    SimulatedAudioBackend fallback) : IAudioBackend
{
    private bool _primaryHealthy = true;

    public Task<AudioMasterState> GetMasterAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(backend => backend.GetMasterAsync(cancellationToken), cancellationToken);
    }

    public Task<AudioMasterState> SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        return ExecuteAsync(backend => backend.SetVolumeAsync(volume, cancellationToken), cancellationToken);
    }

    public Task<AudioMasterState> SetMuteAsync(bool muted, CancellationToken cancellationToken)
    {
        return ExecuteAsync(backend => backend.SetMuteAsync(muted, cancellationToken), cancellationToken);
    }

    public Task<AudioMasterState> ToggleMuteAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(backend => backend.ToggleMuteAsync(cancellationToken), cancellationToken);
    }

    public Task<AudioMasterState> SampleAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(backend => backend.SampleAsync(cancellationToken), cancellationToken);
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _primaryHealthy = true;
        await primary.RestartAsync(cancellationToken);
        await fallback.RestartAsync(cancellationToken);
    }

    public void Dispose()
    {
        primary.Dispose();
        fallback.Dispose();
    }

    private async Task<AudioMasterState> ExecuteAsync(
        Func<IAudioBackend, Task<AudioMasterState>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_primaryHealthy)
        {
            try
            {
                return await operation(primary);
            }
            catch (Exception ex)
            {
                _primaryHealthy = false;
                logger.LogWarning(ex, "Primary WASAPI backend failed, switching to simulated fallback.");
            }
        }

        return await operation(fallback);
    }
}
