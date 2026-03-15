using VolumePadLink.Agent.Services.Device;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class DeviceProtocolCodecTests
{
    [Fact]
    public void ParseEnvelope_ValidMessage_Succeeds()
    {
        var codec = new DeviceProtocolCodec();

        var ok = codec.TryParseEnvelope("{\"type\":\"input.event\",\"payload\":{\"controlId\":\"encoder-main\",\"eventType\":\"rotate\",\"delta\":1}}", out var envelope, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(envelope);
        Assert.Equal("input.event", envelope!.Type);
    }

    [Fact]
    public void ParseEnvelope_InvalidMessage_FailsSafely()
    {
        var codec = new DeviceProtocolCodec();

        var ok = codec.TryParseEnvelope("not-json", out var envelope, out var error);

        Assert.False(ok);
        Assert.Null(envelope);
        Assert.NotNull(error);
    }
}
