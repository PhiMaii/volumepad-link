using VolumePadLink.Agent.Services.Device;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class ReconnectPolicyTests
{
    [Theory]
    [InlineData(1, 1500)]
    [InlineData(2, 3000)]
    [InlineData(3, 5000)]
    [InlineData(4, 8000)]
    [InlineData(5, 10000)]
    [InlineData(50, 10000)]
    public void GetDelay_StaysWithinConfiguredBackoffWindow(int attempt, int expectedBaseMs)
    {
        var policy = new ReconnectPolicy();

        var delay = policy.GetDelay(attempt);

        Assert.InRange(delay.TotalMilliseconds, expectedBaseMs - 200, expectedBaseMs + 200);
    }
}
