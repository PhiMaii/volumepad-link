using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.Services.Audio;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.DTOs;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class AudioServiceModeTests
{
    [Fact]
    public async Task SetModeAsync_SwitchesBetweenBackends_AndUpdatesState()
    {
        var state = new AgentStateStore();
        var eventHub = new RecordingEventHub();
        var factory = new FakeAudioBackendFactory();

        var service = new AudioService(
            state,
            eventHub,
            factory,
            Options.Create(new AgentOptions()),
            NullLogger<AudioService>.Instance);

        var firstMode = await service.SetModeAsync(AudioMode.Real);
        var secondMode = await service.SetModeAsync(AudioMode.Simulated);

        Assert.Equal(AudioMode.Real, firstMode);
        Assert.Equal(AudioMode.Simulated, secondMode);
        Assert.Equal(AudioMode.Simulated, state.GetAudioMode());
        Assert.Contains(AudioMode.Real, factory.CreatedModes);
        Assert.Contains(AudioMode.Simulated, factory.CreatedModes);
    }

    [Fact]
    public async Task SetModeAsync_FallsBackToSimulated_WhenRealInitializationFails()
    {
        var state = new AgentStateStore();
        var eventHub = new RecordingEventHub();
        var factory = new FakeAudioBackendFactory(failRealInitialization: true);

        var service = new AudioService(
            state,
            eventHub,
            factory,
            Options.Create(new AgentOptions()),
            NullLogger<AudioService>.Instance);

        var effective = await service.SetModeAsync(AudioMode.Real);

        Assert.Equal(AudioMode.Simulated, effective);
        Assert.Equal(AudioMode.Simulated, state.GetAudioMode());
        Assert.Contains(eventHub.Published.Select(e => e.Name), name => name == EventNames.DiagnosticsError);
    }

    private sealed class FakeAudioBackendFactory(bool failRealInitialization = false) : IAudioBackendFactory
    {
        public List<AudioMode> CreatedModes { get; } = [];

        public IAudioBackend Create(AudioMode mode)
        {
            CreatedModes.Add(mode);

            return mode switch
            {
                AudioMode.Real => new FakeBackend(mode, failRealInitialization),
                _ => new FakeBackend(mode, false)
            };
        }
    }

    private sealed class FakeBackend(AudioMode mode, bool failInitialization) : IAudioBackend
    {
        public AudioMode Mode => mode;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (failInitialization)
            {
                throw new InvalidOperationException("Real backend init failed");
            }

            return Task.CompletedTask;
        }

        public Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default)
        {
            var graph = new AudioGraphDto(
                new MasterAudioDto("default", "Default", 0.5f, false, 0f, 0f),
                [],
                DateTimeOffset.UtcNow);
            return Task.FromResult(graph);
        }

        public Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingEventHub : IEventHub
    {
        public List<IpcMessage> Published { get; } = [];

        public event Func<IpcMessage, Task>? EventPublished { add { } remove { } }

        public Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        {
            var message = new IpcMessage
            {
                Type = IpcMessageKinds.Event,
                Name = eventName,
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload)
            };

            Published.Add(message);
            return Task.CompletedTask;
        }
    }
}

