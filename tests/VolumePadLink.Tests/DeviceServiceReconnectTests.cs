using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.Services.Device;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.Abstractions;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class DeviceServiceReconnectTests
{
    [Fact]
    public async Task ManualDisconnect_DoesNotStartReconnectLoop()
    {
        var state = new AgentStateStore();
        var eventHub = new RecordingEventHub();
        var codec = new DeviceProtocolCodec();
        var scheduler = new OutboundMessageScheduler();

        var service = new DeviceService(
            codec,
            state,
            eventHub,
            scheduler,
            new FastReconnectPolicy(),
            Options.Create(new AgentOptions()),
            NullLogger<DeviceService>.Instance,
            NullLogger<SimulatedDeviceSession>.Instance);

        var connected = await service.ConnectAsync("sim");
        Assert.True(connected.IsConnected);

        await service.DisconnectAsync();
        await Task.Delay(350);

        var status = await service.GetStatusAsync();
        Assert.False(status.IsConnected);

        var reconnectWarnings = eventHub.Published
            .Where(x => x.Name == EventNames.DiagnosticsWarning)
            .Select(x => x.Payload.GetRawText())
            .ToList();

        Assert.DoesNotContain(reconnectWarnings, text => text.Contains("reconnect", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FastReconnectPolicy : IReconnectPolicy
    {
        public TimeSpan GetDelay(int attempt) => TimeSpan.FromMilliseconds(10);
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
