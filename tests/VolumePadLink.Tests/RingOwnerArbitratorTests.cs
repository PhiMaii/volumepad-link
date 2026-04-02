using VolumePadLink.Agent.Services.Ring;

namespace VolumePadLink.Tests;

public sealed class RingOwnerArbitratorTests
{
    [Fact]
    public void BeginAndEndAnimation_TransitionsBetweenMeterAndAnimation()
    {
        var arbitrator = new RingOwnerArbitrator();

        arbitrator.BeginAnimation("stream-1");
        Assert.Equal(RingRenderOwner.Animation, arbitrator.Owner);
        Assert.Equal("stream-1", arbitrator.ActiveStreamId);

        arbitrator.EndAnimation("stream-1");
        Assert.Equal(RingRenderOwner.Meter, arbitrator.Owner);
        Assert.Null(arbitrator.ActiveStreamId);
    }

    [Fact]
    public void MuteOverride_HasPriority_AndFallsBackToAnimation()
    {
        var arbitrator = new RingOwnerArbitrator();
        var now = DateTimeOffset.UtcNow;

        arbitrator.BeginAnimation("stream-2");
        arbitrator.TriggerMuteOverride(100, now);
        Assert.Equal(RingRenderOwner.MuteOverride, arbitrator.Owner);

        arbitrator.Tick(now.AddMilliseconds(50));
        Assert.Equal(RingRenderOwner.MuteOverride, arbitrator.Owner);

        arbitrator.Tick(now.AddMilliseconds(101));
        Assert.Equal(RingRenderOwner.Animation, arbitrator.Owner);
    }
}
