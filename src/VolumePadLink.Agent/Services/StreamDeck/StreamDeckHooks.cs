using VolumePadLink.Agent.Core;
using VolumePadLink.Contracts.Protocol;
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
    ISettingsService settingsService,
    IStreamDeckStateProvider stateProvider) : IStreamDeckCommandService
{
    public async Task<StreamDeckState> ToggleMuteAsync(CancellationToken cancellationToken)
    {
        await audioService.ToggleMuteAsync(cancellationToken);
        return stateProvider.GetStateSnapshot();
    }

    public async Task<StreamDeckState> AdjustVolumeByStepAsync(double step, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(step))
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "step must be a finite number.");
        }

        if (Math.Abs(step) < 0.001 || Math.Abs(step) > 0.20)
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.OutOfRange, "step must be between 0.001 and 0.20.");
        }

        var current = await audioService.GetMasterAsync(cancellationToken);
        var target = Math.Clamp(current.Volume + step, 0.0, 1.0);
        await audioService.SetVolumeAsync(target, cancellationToken);
        return stateProvider.GetStateSnapshot();
    }

    public StreamDeckSettingsSnapshot GetSettingsSnapshot()
    {
        return new StreamDeckSettingsSnapshot
        {
            Effective = settingsService.GetEffectiveSettings(),
            State = stateProvider.GetStateSnapshot(),
        };
    }

    public async Task<StreamDeckSettingsSnapshot> UpdateSettingsAsync(StreamDeckSettingsPatch patch, CancellationToken cancellationToken)
    {
        var merged = ApplyPatch(settingsService.GetEffectiveSettings(), patch);
        var response = await settingsService.ApplyAsync(merged, cancellationToken);

        return new StreamDeckSettingsSnapshot
        {
            Effective = response.Effective,
            State = stateProvider.GetStateSnapshot(),
        };
    }

    private static AppSettings ApplyPatch(AppSettings baseline, StreamDeckSettingsPatch patch)
    {
        var merged = baseline.Clone();

        merged.AutoReconnectOnError = patch.AutoReconnectOnError ?? merged.AutoReconnectOnError;
        merged.AutoConnectOnStartup = patch.AutoConnectOnStartup ?? merged.AutoConnectOnStartup;
        merged.VolumeStepSize = patch.VolumeStepSize ?? merged.VolumeStepSize;
        merged.DetentCount = patch.DetentCount ?? merged.DetentCount;
        merged.DetentStrength = patch.DetentStrength ?? merged.DetentStrength;
        merged.SnapStrength = patch.SnapStrength ?? merged.SnapStrength;
        merged.EncoderInvert = patch.EncoderInvert ?? merged.EncoderInvert;
        merged.LedBrightness = patch.LedBrightness ?? merged.LedBrightness;

        merged.MeterMode = string.IsNullOrWhiteSpace(patch.MeterMode) ? merged.MeterMode : patch.MeterMode;
        merged.MeterColor = string.IsNullOrWhiteSpace(patch.MeterColor) ? merged.MeterColor : patch.MeterColor;
        merged.MeterBrightness = patch.MeterBrightness ?? merged.MeterBrightness;
        merged.MeterGain = patch.MeterGain ?? merged.MeterGain;
        merged.MeterSmoothing = patch.MeterSmoothing ?? merged.MeterSmoothing;
        merged.MeterPeakHoldMs = patch.MeterPeakHoldMs ?? merged.MeterPeakHoldMs;
        merged.MeterMuteRedDurationMs = patch.MeterMuteRedDurationMs ?? merged.MeterMuteRedDurationMs;

        merged.LowEndstopEnabled = patch.LowEndstopEnabled ?? merged.LowEndstopEnabled;
        merged.LowEndstopPosition = patch.LowEndstopPosition ?? merged.LowEndstopPosition;
        merged.LowEndstopStrength = patch.LowEndstopStrength ?? merged.LowEndstopStrength;

        merged.HighEndstopEnabled = patch.HighEndstopEnabled ?? merged.HighEndstopEnabled;
        merged.HighEndstopPosition = patch.HighEndstopPosition ?? merged.HighEndstopPosition;
        merged.HighEndstopStrength = patch.HighEndstopStrength ?? merged.HighEndstopStrength;

        return merged;
    }
}
