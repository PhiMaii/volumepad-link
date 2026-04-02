using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Services;
using VolumePadLink.Agent.Services.StreamDeck;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Tests;

public sealed class StreamDeckCommandServiceTests
{
    [Fact]
    public async Task AdjustVolumeByStep_RejectsOutOfRangeStep()
    {
        var audio = new FakeAudioService();
        var settings = new FakeSettingsService();
        var provider = new FakeStreamDeckStateProvider(audio);
        var service = new StreamDeckCommandService(audio, settings, provider);

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => service.AdjustVolumeByStepAsync(0.5, CancellationToken.None));
        Assert.Equal(ProtocolNames.ErrorCodes.OutOfRange, exception.Code);
    }

    [Fact]
    public async Task AdjustVolumeByStep_UpdatesVolumeAndReturnsSnapshot()
    {
        var audio = new FakeAudioService { CurrentVolume = 0.95 };
        var settings = new FakeSettingsService();
        var provider = new FakeStreamDeckStateProvider(audio);
        var service = new StreamDeckCommandService(audio, settings, provider);

        var state = await service.AdjustVolumeByStepAsync(0.10, CancellationToken.None);

        Assert.Equal(1.0, state.Master.Volume, 6);
        Assert.Equal(1.0, audio.CurrentVolume, 6);
        Assert.Equal(1, audio.SetVolumeCalls);
    }

    [Fact]
    public async Task UpdateSettingsAsync_MergesPatchAndPreservesOtherFields()
    {
        var audio = new FakeAudioService();
        var settings = new FakeSettingsService(new AppSettings
        {
            DetentCount = 24,
            DetentStrength = 0.65,
            SnapStrength = 0.40,
            LowEndstopEnabled = true,
            LowEndstopPosition = -1.0,
            LowEndstopStrength = 0.70,
            HighEndstopEnabled = true,
            HighEndstopPosition = 1.0,
            HighEndstopStrength = 0.70,
        });
        var provider = new FakeStreamDeckStateProvider(audio);
        var service = new StreamDeckCommandService(audio, settings, provider);

        var result = await service.UpdateSettingsAsync(new StreamDeckSettingsPatch
        {
            DetentCount = 28,
            DetentStrength = 0.75,
            LowEndstopPosition = -0.85,
            HighEndstopPosition = 0.85,
        }, CancellationToken.None);

        Assert.Equal(28, result.Effective.DetentCount);
        Assert.Equal(0.75, result.Effective.DetentStrength, 6);
        Assert.Equal(0.40, result.Effective.SnapStrength, 6);
        Assert.Equal(-0.85, result.Effective.LowEndstopPosition, 6);
        Assert.Equal(0.70, result.Effective.LowEndstopStrength, 6);
        Assert.Equal(0.85, result.Effective.HighEndstopPosition, 6);
    }

    private sealed class FakeAudioService : IAudioService
    {
        public double CurrentVolume { get; set; } = 0.5;
        public bool CurrentMuted { get; set; }
        public int SetVolumeCalls { get; private set; }

        public Task<AudioMasterState> GetMasterAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(State());
        }

        public Task<AudioMasterState> SetVolumeAsync(double volume, CancellationToken cancellationToken)
        {
            SetVolumeCalls++;
            CurrentVolume = Math.Clamp(volume, 0.0, 1.0);
            return Task.FromResult(State());
        }

        public Task<AudioMasterState> SetMuteAsync(bool muted, CancellationToken cancellationToken)
        {
            CurrentMuted = muted;
            return Task.FromResult(State());
        }

        public Task<AudioMasterState> ToggleMuteAsync(CancellationToken cancellationToken)
        {
            CurrentMuted = !CurrentMuted;
            return Task.FromResult(State());
        }

        public Task<AudioMasterState> SampleMeterAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(State());
        }

        public Task RestartBackendAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private AudioMasterState State()
        {
            return new AudioMasterState(CurrentVolume, CurrentMuted, 0.0, 0.0, DateTimeOffset.UtcNow);
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private AppSettings _effective;

        public FakeSettingsService(AppSettings? initial = null)
        {
            _effective = (initial ?? new AppSettings()).Clone();
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public AppSettings GetEffectiveSettings()
        {
            return _effective.Clone();
        }

        public Task<SettingsUpdateResponse> ApplyAsync(AppSettings incomingSettings, CancellationToken cancellationToken)
        {
            var validation = SettingsValidator.ValidateAndNormalize(incomingSettings);
            if (!validation.IsValid)
            {
                throw new ProtocolException(ProtocolNames.ErrorCodes.OutOfRange, string.Join(" ", validation.Errors));
            }

            _effective = validation.Effective.Clone();
            return Task.FromResult(new SettingsUpdateResponse(_effective.Clone()));
        }
    }

    private sealed class FakeStreamDeckStateProvider(FakeAudioService audioService) : IStreamDeckStateProvider
    {
        public StreamDeckState GetStateSnapshot()
        {
            return new StreamDeckState
            {
                Master = new StreamDeckMasterState
                {
                    Volume = audioService.CurrentVolume,
                    Muted = audioService.CurrentMuted,
                },
                DeviceConnection = new StreamDeckDeviceConnection
                {
                    State = ConnectionState.Disconnected,
                    PortName = null,
                },
                CapturedAtUtc = DateTimeOffset.UtcNow,
            };
        }
    }
}
