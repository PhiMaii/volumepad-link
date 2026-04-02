using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Transport.Device;

public interface IDeviceLink : IAsyncDisposable
{
    event EventHandler<ProtocolEnvelope>? MessageReceived;

    bool IsConnected { get; }
    string? PortName { get; }

    Task ConnectAsync(string portName, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task SendAsync(ProtocolEnvelope message, CancellationToken cancellationToken);
}
