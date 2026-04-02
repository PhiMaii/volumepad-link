using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services;

public interface IDeviceService
{
    event EventHandler<DeviceButtonInput>? ButtonInputReceived;
    event EventHandler<DeviceEncoderInput>? EncoderInputReceived;
    event EventHandler<DebugState>? DebugStateReceived;

    Task<DeviceListPortsResponse> ListPortsAsync(CancellationToken cancellationToken);
    Task<DeviceStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<DeviceStatus> ConnectAsync(string portName, CancellationToken cancellationToken);
    Task<DeviceStatus> DisconnectAsync(CancellationToken cancellationToken);
    Task<DeviceStatus> ReconnectAsync(CancellationToken cancellationToken);

    Task QueueCommandAsync(string name, object payload, DeviceCommandPriority priority, CancellationToken cancellationToken);
    Task<TResponse> SendRequestAsync<TResponse>(string name, object payload, DeviceCommandPriority priority, CancellationToken cancellationToken);
}
