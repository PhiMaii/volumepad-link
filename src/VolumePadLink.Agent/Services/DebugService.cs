using VolumePadLink.Agent.Core;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Services;

public sealed class DebugService : IDebugService
{
    private readonly IDeviceService _deviceService;
    private readonly RuntimeStateStore _stateStore;
    private readonly EventBus _eventBus;
    private readonly ILogger<DebugService> _logger;

    public DebugService(
        IDeviceService deviceService,
        RuntimeStateStore stateStore,
        EventBus eventBus,
        ILogger<DebugService> logger)
    {
        _deviceService = deviceService;
        _stateStore = stateStore;
        _eventBus = eventBus;
        _logger = logger;

        _deviceService.DebugStateReceived += OnDebugStateReceived;
    }

    public async Task<DebugState> GetStateAsync(CancellationToken cancellationToken)
    {
        var state = await _deviceService.SendRequestAsync<DebugState>(
            ProtocolNames.DeviceMethods.DebugGetState,
            ProtocolJson.EmptyObject,
            DeviceCommandPriority.Medium,
            cancellationToken);

        await PublishDebugStateAsync(state, cancellationToken);
        return state;
    }

    public async Task<DebugState> ApplyTuningAsync(DebugTuning tuning, CancellationToken cancellationToken)
    {
        await _deviceService.SendRequestAsync<object>(
            ProtocolNames.DeviceMethods.DebugApplyTuning,
            tuning,
            DeviceCommandPriority.Medium,
            cancellationToken);

        return await GetStateAsync(cancellationToken);
    }

    public async Task<DebugState> SetStreamAsync(bool enabled, int intervalMs, CancellationToken cancellationToken)
    {
        var effectiveIntervalMs = Math.Max(30, intervalMs);
        await _deviceService.SendRequestAsync<object>(
            ProtocolNames.DeviceMethods.DebugSetStream,
            new DebugSetStreamRequest(enabled, effectiveIntervalMs),
            DeviceCommandPriority.Medium,
            cancellationToken);

        _stateStore.UpdateDebugStream(enabled, effectiveIntervalMs);
        return await GetStateAsync(cancellationToken);
    }

    private void OnDebugStateReceived(object? sender, DebugState state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await PublishDebugStateAsync(state, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed publishing debug state.");
            }
        });
    }

    private async Task PublishDebugStateAsync(DebugState state, CancellationToken cancellationToken)
    {
        _stateStore.UpdateDebugState(state);
        await _eventBus.PublishAsync(
            ProtocolNames.Events.DebugState,
            new DebugStateEvent(state),
            cancellationToken);
    }
}
