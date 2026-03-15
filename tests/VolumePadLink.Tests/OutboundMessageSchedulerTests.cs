using VolumePadLink.Agent.Services.Device;
using VolumePadLink.Agent.Services.Interfaces;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class OutboundMessageSchedulerTests
{
    [Fact]
    public async Task DequeueAsync_DrainsHighThenNormalThenLow()
    {
        var scheduler = new OutboundMessageScheduler();

        await scheduler.EnqueueAsync(new OutboundMessage("low", 1), OutboundPriority.Low);
        await scheduler.EnqueueAsync(new OutboundMessage("normal", 1), OutboundPriority.Normal);
        await scheduler.EnqueueAsync(new OutboundMessage("high", 1), OutboundPriority.High);

        var first = await scheduler.DequeueAsync();
        var second = await scheduler.DequeueAsync();
        var third = await scheduler.DequeueAsync();

        Assert.Equal("high", first.Line);
        Assert.Equal("normal", second.Line);
        Assert.Equal("low", third.Line);
    }

    [Fact]
    public async Task LowPriorityQueue_DropsOldest_WhenCapacityExceeded()
    {
        var scheduler = new OutboundMessageScheduler();

        for (var i = 0; i < 33; i++)
        {
            await scheduler.EnqueueAsync(new OutboundMessage($"low-{i}", 1), OutboundPriority.Low);
        }

        var lines = new List<string>();
        for (var i = 0; i < 32; i++)
        {
            var next = await scheduler.DequeueAsync();
            lines.Add(next.Line);
        }

        var depth = scheduler.GetDepth();

        Assert.DoesNotContain("low-0", lines);
        Assert.Contains("low-32", lines);
        Assert.Equal(1, depth.DroppedLow);
        Assert.Equal(0, depth.Low);
    }
}
