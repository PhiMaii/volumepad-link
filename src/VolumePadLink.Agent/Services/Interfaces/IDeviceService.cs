using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface IDeviceService
{
    event Func<DeviceInputEventDto, Task>? InputEventReceived;
    event Func<DeviceStatusDto, Task>? DeviceStatusChanged;
    event Func<DeviceCapabilitiesDto, Task>? CapabilitiesReceived;

    Task<DeviceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<DeviceStatusDto> ConnectAsync(string? portName = null, CancellationToken cancellationToken = default);

    Task<DeviceStatusDto> DisconnectAsync(CancellationToken cancellationToken = default);

    Task<DeviceCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    Task ApplySettingsAsync(DeviceSettingsDto settings, CancellationToken cancellationToken = default);

    Task SendDisplayModelAsync(DisplayModelDto model, CancellationToken cancellationToken = default);

    Task SendLedMeterAsync(LedMeterModelDto model, CancellationToken cancellationToken = default);

    Task SendRawMessageAsync(string type, object payload, CancellationToken cancellationToken = default);
}
