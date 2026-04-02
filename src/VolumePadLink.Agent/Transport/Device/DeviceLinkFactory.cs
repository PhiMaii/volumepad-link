using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Options;

namespace VolumePadLink.Agent.Transport.Device;

public sealed class DeviceLinkFactory(
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ILoggerFactory loggerFactory) : IDeviceLinkFactory
{
    public string SimulatorPortName => runtimeOptions.Value.SimulatorPortName;

    public bool IsSimulatorPort(string portName)
    {
        return string.Equals(portName, SimulatorPortName, StringComparison.OrdinalIgnoreCase);
    }

    public IDeviceLink Create(string portName)
    {
        if (IsSimulatorPort(portName))
        {
            return new SimulatedDeviceLink(
                SimulatorPortName,
                loggerFactory.CreateLogger<SimulatedDeviceLink>());
        }

        return new SerialDeviceLink(
            loggerFactory.CreateLogger<SerialDeviceLink>());
    }
}
