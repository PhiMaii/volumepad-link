using VolumePadLink.Agent.Core;
using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services.StreamDeck;

public sealed class StreamDeckStateProvider(RuntimeStateStore stateStore) : IStreamDeckStateProvider
{
    public StreamDeckState GetStateSnapshot()
    {
        var snapshot = stateStore.GetSnapshot();
        return new StreamDeckState
        {
            Master = new StreamDeckMasterState
            {
                Volume = snapshot.AudioMaster.Volume,
                Muted = snapshot.AudioMaster.Muted,
            },
            DeviceConnection = new StreamDeckDeviceConnection
            {
                State = snapshot.DeviceStatus.ConnectionState,
                PortName = snapshot.DeviceStatus.PortName,
            },
            CapturedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}

public sealed class StreamDeckCommandService(
    IAudioService audioService,
    IStreamDeckStateProvider stateProvider) : IStreamDeckCommandService
{
    public async Task<StreamDeckState> ToggleMuteAsync(CancellationToken cancellationToken)
    {
        await audioService.ToggleMuteAsync(cancellationToken);
        return stateProvider.GetStateSnapshot();
    }

    public async Task<StreamDeckState> AdjustVolumeByStepAsync(double step, CancellationToken cancellationToken)
    {
        var current = await audioService.GetMasterAsync(cancellationToken);
        var target = Math.Clamp(current.Volume + step, 0.0, 1.0);
        await audioService.SetVolumeAsync(target, cancellationToken);
        return stateProvider.GetStateSnapshot();
    }
}
