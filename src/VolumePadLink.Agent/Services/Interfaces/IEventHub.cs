using VolumePadLink.Contracts.Abstractions;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface IEventHub
{
    event Func<IpcMessage, Task>? EventPublished;

    Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default);
}
