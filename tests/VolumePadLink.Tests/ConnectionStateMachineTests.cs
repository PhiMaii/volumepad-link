using VolumePadLink.Agent.Services;
using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Tests;

public sealed class ConnectionStateMachineTests
{
    [Theory]
    [InlineData(ConnectionState.Disconnected, ConnectionState.Connecting, true)]
    [InlineData(ConnectionState.Disconnected, ConnectionState.Connected, false)]
    [InlineData(ConnectionState.Connecting, ConnectionState.Connected, true)]
    [InlineData(ConnectionState.Connected, ConnectionState.Reconnecting, true)]
    [InlineData(ConnectionState.Connected, ConnectionState.Connecting, false)]
    [InlineData(ConnectionState.Error, ConnectionState.Reconnecting, true)]
    [InlineData(ConnectionState.Error, ConnectionState.Connected, false)]
    public void IsTransitionAllowed_ReturnsExpectedValues(ConnectionState from, ConnectionState to, bool expected)
    {
        var allowed = ConnectionStateMachine.IsTransitionAllowed(from, to);
        Assert.Equal(expected, allowed);
    }
}
