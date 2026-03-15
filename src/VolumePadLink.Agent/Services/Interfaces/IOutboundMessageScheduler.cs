namespace VolumePadLink.Agent.Services.Interfaces;

public enum OutboundPriority
{
    High,
    Normal,
    Low
}

public readonly record struct OutboundQueueDepth(int High, int Normal, int Low, long DroppedLow);

public readonly record struct OutboundMessage(string Line, int Generation);

public interface IOutboundMessageScheduler
{
    Task EnqueueAsync(OutboundMessage message, OutboundPriority priority, CancellationToken cancellationToken = default);

    ValueTask<OutboundMessage> DequeueAsync(CancellationToken cancellationToken = default);

    OutboundQueueDepth GetDepth();
}
