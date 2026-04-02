using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services;

public static class ConnectionStateMachine
{
    public static bool IsTransitionAllowed(ConnectionState from, ConnectionState to)
    {
        if (from == to)
        {
            return true;
        }

        return from switch
        {
            ConnectionState.Disconnected => to is ConnectionState.Connecting,
            ConnectionState.Connecting => to is ConnectionState.Connected or ConnectionState.Error or ConnectionState.Disconnected,
            ConnectionState.Connected => to is ConnectionState.Reconnecting or ConnectionState.Error or ConnectionState.Disconnected,
            ConnectionState.Reconnecting => to is ConnectionState.Connected or ConnectionState.Error or ConnectionState.Disconnected,
            ConnectionState.Error => to is ConnectionState.Reconnecting or ConnectionState.Connecting or ConnectionState.Disconnected,
            _ => false,
        };
    }
}
