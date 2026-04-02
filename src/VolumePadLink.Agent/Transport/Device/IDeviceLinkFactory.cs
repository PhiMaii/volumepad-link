namespace VolumePadLink.Agent.Transport.Device;

public interface IDeviceLinkFactory
{
    string SimulatorPortName { get; }
    bool IsSimulatorPort(string portName);
    IDeviceLink Create(string portName);
}
