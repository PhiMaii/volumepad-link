using VolumePadLink.Agent.Services.Interfaces;

namespace VolumePadLink.Agent.Services.Device;

public sealed class OutboundMessageScheduler : IOutboundMessageScheduler
{
    private const int HighCapacity = 256;
    private const int NormalCapacity = 128;
    private const int LowCapacity = 32;

    private readonly object _sync = new();
    private readonly Queue<OutboundMessage> _high = new();
    private readonly Queue<OutboundMessage> _normal = new();
    private readonly Queue<OutboundMessage> _low = new();

    private readonly SemaphoreSlim _availableItems = new(0);
    private readonly SemaphoreSlim _highSlots = new(HighCapacity, HighCapacity);
    private readonly SemaphoreSlim _normalSlots = new(NormalCapacity, NormalCapacity);

    private long _droppedLow;

    public async Task EnqueueAsync(OutboundMessage message, OutboundPriority priority, CancellationToken cancellationToken = default)
    {
        switch (priority)
        {
            case OutboundPriority.High:
                await _highSlots.WaitAsync(cancellationToken);
                lock (_sync)
                {
                    _high.Enqueue(message);
                }

                _availableItems.Release();
                return;

            case OutboundPriority.Normal:
                await _normalSlots.WaitAsync(cancellationToken);
                lock (_sync)
                {
                    _normal.Enqueue(message);
                }

                _availableItems.Release();
                return;

            default:
                var shouldSignal = false;
                lock (_sync)
                {
                    if (_low.Count >= LowCapacity)
                    {
                        _low.Dequeue();
                        _droppedLow++;
                    }
                    else
                    {
                        shouldSignal = true;
                    }

                    _low.Enqueue(message);
                }

                if (shouldSignal)
                {
                    _availableItems.Release();
                }

                return;
        }
    }

    public async ValueTask<OutboundMessage> DequeueAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await _availableItems.WaitAsync(cancellationToken);

            lock (_sync)
            {
                if (_high.Count > 0)
                {
                    var message = _high.Dequeue();
                    _highSlots.Release();
                    return message;
                }

                if (_normal.Count > 0)
                {
                    var message = _normal.Dequeue();
                    _normalSlots.Release();
                    return message;
                }

                if (_low.Count > 0)
                {
                    return _low.Dequeue();
                }
            }
        }
    }

    public OutboundQueueDepth GetDepth()
    {
        lock (_sync)
        {
            return new OutboundQueueDepth(_high.Count, _normal.Count, _low.Count, _droppedLow);
        }
    }
}
