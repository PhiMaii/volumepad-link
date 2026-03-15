using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VolumePadLink.Agent.IPC;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using VolumePadLink.Contracts.DTOs;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class CommandRouterSettingsTests
{
    [Fact]
    public async Task SettingsUpdate_UpdatesAudioMode_AndDeviceSettings()
    {
        var audio = new FakeAudioService();
        var target = new FakeTargetService();
        var device = new FakeDeviceService();
        var settingsStore = new FakeSettingsStore();
        var state = new AgentStateStore();
        var events = new FakeEventHub();

        var router = new CommandRouter(audio, target, device, settingsStore, state, events, NullLogger<CommandRouter>.Instance);

        var deviceSettings = new DeviceSettingsDto(24, 0.7f, 0.4f, 0.9f, 0.9f, false, 450);
        var appSettings = new AppSettingsDto(deviceSettings, AudioMode.Simulated);
        var request = new UpdateSettingsRequest(appSettings);

        var command = new IpcMessage
        {
            Type = IpcMessageKinds.Command,
            Id = "settings-1",
            Name = CommandNames.SettingsUpdate,
            Payload = JsonSerializer.SerializeToElement(request)
        };

        var response = await router.HandleAsync(command, CancellationToken.None);
        var settingsResponse = JsonSerializer.Deserialize<SettingsResponse>(response.Payload.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(settingsResponse);
        Assert.Equal(AudioMode.Simulated, settingsResponse!.Settings.AudioMode);
        Assert.Equal(AudioMode.Simulated, audio.CurrentMode);
        Assert.Equal(deviceSettings.DetentCount, device.LastAppliedSettings?.DetentCount);
        Assert.Equal(AudioMode.Simulated, settingsStore.LastSaved?.AudioMode);
    }

    private sealed class FakeAudioService : IAudioService
    {
        public event Func<AudioGraphDto, Task>? GraphChanged { add { } remove { } }

        public AudioMode CurrentMode { get; private set; } = AudioMode.Real;

        public Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AudioGraphDto(new MasterAudioDto("default", "Default", 0.5f, false, 0f, 0f), [], DateTimeOffset.UtcNow));
        }

        public Task<AudioMode> GetModeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CurrentMode);
        }

        public Task<AudioMode> SetModeAsync(AudioMode mode, CancellationToken cancellationToken = default)
        {
            CurrentMode = mode;
            return Task.FromResult(CurrentMode);
        }

        public Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTargetService : ITargetService
    {
        public Task<ActiveTargetDto> GetActiveTargetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ActiveTargetDto(TargetKinds.Master, null, null));
        }

        public Task<ActiveTargetDto> SelectTargetAsync(ActiveTargetDto target, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(target);
        }

        public Task<float> ChangeActiveTargetVolumeAsync(float delta, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0.5f);
        }

        public Task<bool> ToggleActiveTargetMuteAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task EnsureTargetAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceService : IDeviceService
    {
        public event Func<DeviceInputEventDto, Task>? InputEventReceived { add { } remove { } }
        public event Func<DeviceStatusDto, Task>? DeviceStatusChanged { add { } remove { } }
        public event Func<DeviceCapabilitiesDto, Task>? CapabilitiesReceived { add { } remove { } }

        public DeviceSettingsDto? LastAppliedSettings { get; private set; }

        public Task<DeviceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DeviceStatusDto(false, null, null, null, null, null));
        }

        public Task<DeviceStatusDto> ConnectAsync(string? portName = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DeviceStatusDto(true, portName, "dev", null, DateTimeOffset.UtcNow, null));
        }

        public Task<DeviceStatusDto> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DeviceStatusDto(false, null, null, null, DateTimeOffset.UtcNow, null));
        }

        public Task<DeviceCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DeviceCapabilitiesDto?>(null);
        }

        public Task ApplySettingsAsync(DeviceSettingsDto settings, CancellationToken cancellationToken = default)
        {
            LastAppliedSettings = settings;
            return Task.CompletedTask;
        }

        public Task SendDisplayModelAsync(DisplayModelDto model, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendLedMeterAsync(LedMeterModelDto model, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendRawMessageAsync(string type, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public StoredAgentSettings? LastSaved { get; private set; }

        public Task<StoredAgentSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StoredAgentSettings(
                new ActiveTargetDto(TargetKinds.Master, null, null),
                new DeviceSettingsDto(24, 0.65f, 0.4f, 0.8f, 0.8f, false, 450),
                AudioMode.Real,
                null));
        }

        public Task SaveAsync(StoredAgentSettings settings, CancellationToken cancellationToken = default)
        {
            LastSaved = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEventHub : IEventHub
    {
        public event Func<IpcMessage, Task>? EventPublished { add { } remove { } }

        public Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}


