using System.Threading.Channels;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Options;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Core;

public sealed class EventBus
{
    private readonly Channel<ProtocolEnvelope> _channel;

    public EventBus(IOptions<QueueOptions> queueOptions)
    {
        var options = queueOptions.Value;
        _channel = Channel.CreateBounded<ProtocolEnvelope>(new BoundedChannelOptions(Math.Max(32, options.EventCapacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ValueTask PublishAsync(string name, object payload, CancellationToken cancellationToken)
    {
        var envelope = new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Event,
            Name = name,
            TsUtc = DateTimeOffset.UtcNow,
            Payload = ProtocolJson.ToElement(payload),
        };

        return _channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    public IAsyncEnumerable<ProtocolEnvelope> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
