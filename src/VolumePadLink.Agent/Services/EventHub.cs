using System.Text.Json;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Agent.Services.Interfaces;

namespace VolumePadLink.Agent.Services;

public sealed class EventHub : IEventHub
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public event Func<IpcMessage, Task>? EventPublished;

    public async Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        var message = new IpcMessage
        {
            Type = IpcMessageKinds.Event,
            Name = eventName,
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
        };

        var handlers = EventPublished;
        if (handlers is null)
        {
            return;
        }

        foreach (Func<IpcMessage, Task> handler in handlers.GetInvocationList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler(message);
        }
    }
}
