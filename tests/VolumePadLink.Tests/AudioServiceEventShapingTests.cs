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

public sealed class AudioServiceEventShapingTests
{
    [Fact]
    public async Task SessionVolumeChange_BelowEpsilon_DoesNotEmitSessionUpdated()
    {
        var state = new AgentStateStore();
        var eventHub = new RecordingEventHub();
        var factory = new StatefulBackendFactory();

        var service = new AudioService(
            state,
            eventHub,
            factory,
            Options.Create(new AgentOptions()),
            NullLogger<AudioService>.Instance);

        await service.SetModeAsync(AudioMode.Simulated);
        eventHub.Published.Clear();

        await service.SetSessionVolumeAsync("session-1", 0.503f);

        Assert.DoesNotContain(eventHub.Published, x => x.Name == EventNames.AudioSessionUpdated);
        Assert.DoesNotContain(eventHub.Published, x => x.Name == EventNames.AudioGraphChanged);
    }

    [Fact]
    public async Task GraphChanged_IsThrottled_WhileSessionUpdatesRemainImmediate()
    {
        var state = new AgentStateStore();
        var eventHub = new RecordingEventHub();
        var factory = new StatefulBackendFactory();

        var service = new AudioService(
            state,
            eventHub,
            factory,
            Options.Create(new AgentOptions()),
            NullLogger<AudioService>.Instance);

        await service.SetModeAsync(AudioMode.Simulated);
        eventHub.Published.Clear();

        await service.SetSessionVolumeAsync("session-1", 0.60f);
        await service.SetSessionVolumeAsync("session-1", 0.70f);
        await service.SetSessionVolumeAsync("session-1", 0.80f);

        var graphChangedCount = eventHub.Published.Count(x => x.Name == EventNames.AudioGraphChanged);
        var sessionUpdatedCount = eventHub.Published.Count(x => x.Name == EventNames.AudioSessionUpdated);

        Assert.InRange(graphChangedCount, 0, 1);
        Assert.Equal(3, sessionUpdatedCount);
    }

    private sealed class StatefulBackendFactory : IAudioBackendFactory
    {
        public IAudioBackend Create(AudioMode mode)
        {
            return new StatefulBackend();
        }
    }

    private sealed class StatefulBackend : IAudioBackend
    {
        private MasterAudioDto _master = new("endpoint", "Default", 0.5f, false, 0f, 0f);
        private AudioSessionDto _session = new("session-1", 42, "app", "App", 0.5f, false, 0f, 0f, null, true);

        public AudioMode Mode => AudioMode.Simulated;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default)
        {
            var graph = new AudioGraphDto(_master, [_session], DateTimeOffset.UtcNow);
            return Task.FromResult(graph);
        }

        public Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default)
        {
            _master = _master with { Volume = value };
            return Task.CompletedTask;
        }

        public Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default)
        {
            _master = _master with { Muted = muted };
            return Task.CompletedTask;
        }

        public Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default)
        {
            if (string.Equals(sessionId, _session.SessionId, StringComparison.OrdinalIgnoreCase))
            {
                _session = _session with { Volume = value };
            }

            return Task.CompletedTask;
        }

        public Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default)
        {
            if (string.Equals(sessionId, _session.SessionId, StringComparison.OrdinalIgnoreCase))
            {
                _session = _session with { Muted = muted };
            }

            return Task.CompletedTask;
        }

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

