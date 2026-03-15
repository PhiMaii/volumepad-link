using Microsoft.Extensions.Logging.Abstractions;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.Services.Target;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.DTOs;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class TargetServiceTests
{
    [Fact]
    public async Task EnsureTargetAvailable_FallsBackToMaster_WhenSessionMissing()
    {
        var audio = new FakeAudioService();
        var state = new AgentStateStore();
        var settings = new FakeSettingsStore();
        var events = new FakeEventHub();

        var service = new TargetService(state, audio, settings, events, NullLogger<TargetService>.Instance);

        audio.SetGraph(CreateGraph([new AudioSessionDto("proc:42", 42, "Spotify", "Spotify", 0.5f, false, 0.5f, 0.4f, null, true)]));
        await service.SelectTargetAsync(new ActiveTargetDto(TargetKinds.SessionById, "proc:42", null));

        audio.SetGraph(CreateGraph([]));
        await service.EnsureTargetAvailableAsync();

        var active = await service.GetActiveTargetAsync();
        Assert.Equal(TargetKinds.Master, active.Kind);
        Assert.Null(active.SessionId);
    }

    private static AudioGraphDto CreateGraph(IReadOnlyList<AudioSessionDto> sessions)
    {
        return new AudioGraphDto(
            new MasterAudioDto("default", "Default", 0.5f, false, 0.2f, 0.1f),
            sessions,
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeAudioService : IAudioService
    {
        private AudioGraphDto _graph = CreateGraph([]);

        public event Func<AudioGraphDto, Task>? GraphChanged;

        public Task<AudioGraphDto> GetGraphAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_graph);
        }

        public Task SetMasterVolumeAsync(float value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetMasterMuteAsync(bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetSessionVolumeAsync(string sessionId, float value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetSessionMuteAsync(string sessionId, bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void SetGraph(AudioGraphDto graph)
        {
            _graph = graph;
            _ = GraphChanged?.Invoke(graph);
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private StoredAgentSettings _settings = new(new ActiveTargetDto(TargetKinds.Master, null, null), new DeviceSettingsDto(24, 0.5f, 0.4f, 0.8f, 0.8f, false, 450), null);

        public Task<StoredAgentSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_settings);
        }

        public Task SaveAsync(StoredAgentSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEventHub : IEventHub
    {
        public event Func<VolumePadLink.Contracts.Abstractions.IpcMessage, Task>? EventPublished { add { } remove { } }

        public Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
