using System.Text.Json;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Options;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Services.StreamDeck;

public sealed class StreamDeckTrafficMonitor(IOptions<AgentRuntimeOptions> runtimeOptions)
{
    private readonly object _gate = new();
    private readonly LinkedList<StreamDeckTrafficEntry> _entries = [];
    private readonly int _capacity = Math.Max(128, runtimeOptions.Value.StreamDeckTrafficCapacity);
    private long _sequence;

    public StreamDeckTrafficEntry Record(
        string direction,
        string transport,
        string name,
        object? payload = null,
        int? statusCode = null,
        string? note = null)
    {
        var entry = new StreamDeckTrafficEntry
        {
            Seq = Interlocked.Increment(ref _sequence),
            Direction = direction,
            Transport = transport,
            Name = name,
            StatusCode = statusCode,
            Note = note,
            Payload = payload is null ? ProtocolJson.EmptyObject : ProtocolJson.ToElement(payload),
            UtcNow = DateTimeOffset.UtcNow,
        };

        lock (_gate)
        {
            _entries.AddLast(entry);
            while (_entries.Count > _capacity)
            {
                _entries.RemoveFirst();
            }
        }

        return entry;
    }

    public StreamDeckTrafficSnapshot GetSnapshot(long sinceSeq, int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, _capacity);
        List<StreamDeckTrafficEntry> entries;
        lock (_gate)
        {
            entries = _entries
                .Where(entry => entry.Seq > sinceSeq)
                .ToList();
        }

        if (entries.Count > safeLimit)
        {
            entries = entries[^safeLimit..];
        }

        return new StreamDeckTrafficSnapshot
        {
            LatestSeq = Interlocked.Read(ref _sequence),
            Entries = entries,
        };
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}

public sealed class StreamDeckTrafficSnapshot
{
    public long LatestSeq { get; set; }
    public IReadOnlyList<StreamDeckTrafficEntry> Entries { get; set; } = [];
}

public sealed class StreamDeckTrafficEntry
{
    public long Seq { get; set; }
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    public string Direction { get; set; } = string.Empty;
    public string Transport { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string? Note { get; set; }
    public JsonElement Payload { get; set; } = ProtocolJson.EmptyObject;
}
