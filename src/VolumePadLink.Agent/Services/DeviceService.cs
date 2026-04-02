using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Options;
using VolumePadLink.Agent.Transport.Device;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Services;

public sealed class DeviceService(
    ILogger<DeviceService> logger,
    IOptions<AgentRuntimeOptions> runtimeOptions,
    IOptions<QueueOptions> queueOptions,
    IDeviceLinkFactory linkFactory,
    RuntimeStateStore stateStore,
    EventBus eventBus) : BackgroundService, IDeviceService
{
    private readonly Channel<ProtocolEnvelope> _highPriorityQueue = Channel.CreateBounded<ProtocolEnvelope>(new BoundedChannelOptions(Math.Max(16, queueOptions.Value.DeviceHighCapacity))
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly Channel<ProtocolEnvelope> _mediumPriorityQueue = Channel.CreateBounded<ProtocolEnvelope>(new BoundedChannelOptions(Math.Max(16, queueOptions.Value.DeviceMediumCapacity))
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly Channel<ProtocolEnvelope> _lowPriorityQueue = Channel.CreateBounded<ProtocolEnvelope>(new BoundedChannelOptions(Math.Max(4, queueOptions.Value.DeviceLowCapacity))
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly ConcurrentDictionary<string, TaskCompletionSource<ProtocolEnvelope>> _pendingResponses = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    private IDeviceLink? _activeLink;
    private string? _lastPortName;
    private CancellationTokenSource? _autoReconnectCts;
    private Task? _autoReconnectTask;
    private TaskCompletionSource<DeviceHello>? _helloWaiter;

    public event EventHandler<DeviceButtonInput>? ButtonInputReceived;
    public event EventHandler<DeviceEncoderInput>? EncoderInputReceived;
    public event EventHandler<DebugState>? DebugStateReceived;

    public async Task<DeviceListPortsResponse> ListPortsAsync(CancellationToken cancellationToken)
    {
        var ports = SerialPort.GetPortNames()
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(static port => new DevicePortInfo(port, false))
            .ToList();

        if (!ports.Any(port => string.Equals(port.PortName, linkFactory.SimulatorPortName, StringComparison.OrdinalIgnoreCase)))
        {
            ports.Insert(0, new DevicePortInfo(linkFactory.SimulatorPortName, false));
        }

        return await Task.FromResult(new DeviceListPortsResponse(ports));
    }

    public Task<DeviceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(stateStore.GetSnapshot().DeviceStatus);
    }

    public async Task<DeviceStatus> ConnectAsync(string portName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "PortName is required.");
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            await StopAutoReconnectAsync();
            await DisconnectInternalAsync(forceState: false, cancellationToken);
            await PublishConnectionStateAsync(ConnectionState.Connecting, portName, reason: null, cancellationToken);

            var link = linkFactory.Create(portName);
            link.MessageReceived += OnDeviceMessageReceived;
            _helloWaiter = new TaskCompletionSource<DeviceHello>(TaskCreationOptions.RunContinuationsAsynchronously);
            await link.ConnectAsync(portName, cancellationToken);

            lock (_gate)
            {
                _activeLink = link;
                _lastPortName = portName;
            }

            DeviceHello? hello = null;
            try
            {
                hello = await _helloWaiter.Task.WaitAsync(TimeSpan.FromMilliseconds(runtimeOptions.Value.DeviceRequestTimeoutMs), cancellationToken);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("Timed out waiting for device.hello on port {Port}", portName);
            }
            finally
            {
                _helloWaiter = null;
            }

            var status = await PublishConnectionStateAsync(
                ConnectionState.Connected,
                portName,
                reason: null,
                cancellationToken,
                hello?.DeviceId,
                hello?.FirmwareVersion);

            logger.LogInformation("Connected to device on {Port}", portName);
            return status;
        }
        catch (Exception ex) when (ex is not ProtocolException)
        {
            logger.LogWarning(ex, "Device connect failed for {Port}", portName);
            await PublishConnectionStateAsync(ConnectionState.Error, portName, ex.Message, cancellationToken);
            await TryStartAutoReconnectAsync();
            throw new ProtocolException(ProtocolNames.ErrorCodes.NotConnected, $"Failed to connect to {portName}: {ex.Message}");
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<DeviceStatus> DisconnectAsync(CancellationToken cancellationToken)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            await StopAutoReconnectAsync();
            await DisconnectInternalAsync(forceState: true, cancellationToken);
            return stateStore.GetSnapshot().DeviceStatus;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<DeviceStatus> ReconnectAsync(CancellationToken cancellationToken)
    {
        string? portName;
        lock (_gate)
        {
            portName = _lastPortName;
        }

        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "No previous port to reconnect.");
        }

        await PublishConnectionStateAsync(ConnectionState.Reconnecting, portName, reason: null, cancellationToken);
        return await ConnectAsync(portName, cancellationToken);
    }

    public async Task QueueCommandAsync(string name, object payload, DeviceCommandPriority priority, CancellationToken cancellationToken)
    {
        if (!IsConnected())
        {
            return;
        }

        var envelope = BuildDeviceRequest(name, payload);
        await WritePriorityAsync(envelope, priority, cancellationToken);
    }

    public async Task<TResponse> SendRequestAsync<TResponse>(string name, object payload, DeviceCommandPriority priority, CancellationToken cancellationToken)
    {
        if (!IsConnected())
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.NotConnected, "Device is not connected.");
        }

        var envelope = BuildDeviceRequest(name, payload);
        var completion = new TaskCompletionSource<ProtocolEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingResponses.TryAdd(envelope.Id!, completion))
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InternalError, "Failed to track device request.");
        }

        try
        {
            await WritePriorityAsync(envelope, priority, cancellationToken);
            var response = await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(runtimeOptions.Value.DeviceRequestTimeoutMs), cancellationToken);

            if (!response.Ok.GetValueOrDefault())
            {
                var error = response.Error ?? new ProtocolError(ProtocolNames.ErrorCodes.InternalError, "Unknown device error.");
                throw new ProtocolException(error.Code, error.Message);
            }

            if (typeof(TResponse) == typeof(object))
            {
                return (TResponse)(object)new object();
            }

            return ProtocolJson.DeserializePayload<TResponse>(response);
        }
        catch (TimeoutException)
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.Timeout, $"Device request '{name}' timed out.");
        }
        finally
        {
            _pendingResponses.TryRemove(envelope.Id!, out _);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ProtocolEnvelope? envelope = null;

            if (_highPriorityQueue.Reader.TryRead(out var high))
            {
                envelope = high;
            }
            else if (_mediumPriorityQueue.Reader.TryRead(out var medium))
            {
                envelope = medium;
            }
            else if (_lowPriorityQueue.Reader.TryRead(out var low))
            {
                envelope = low;
            }

            if (envelope is null)
            {
                await WaitForAnyQueueAsync(stoppingToken);
                continue;
            }

            var link = GetActiveLink();
            if (link is null || !link.IsConnected)
            {
                FailPendingForEnvelope(envelope, new ProtocolException(ProtocolNames.ErrorCodes.NotConnected, "Device disconnected before send."));
                continue;
            }

            try
            {
                await link.SendAsync(envelope, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed sending device command {Name}", envelope.Name);
                FailPendingForEnvelope(envelope, ex);
                await PublishConnectionStateAsync(ConnectionState.Error, link.PortName, ex.Message, stoppingToken);
                await TryStartAutoReconnectAsync();
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopAutoReconnectAsync();
        await DisconnectInternalAsync(forceState: true, cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private IDeviceLink? GetActiveLink()
    {
        lock (_gate)
        {
            return _activeLink;
        }
    }

    private bool IsConnected()
    {
        return GetActiveLink()?.IsConnected == true;
    }

    private ProtocolEnvelope BuildDeviceRequest(string name, object payload)
    {
        return new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Request,
            V = ProtocolConstants.Version,
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            TsUtc = DateTimeOffset.UtcNow,
            Payload = ProtocolJson.ToElement(payload),
        };
    }

    private async Task WritePriorityAsync(ProtocolEnvelope envelope, DeviceCommandPriority priority, CancellationToken cancellationToken)
    {
        switch (priority)
        {
            case DeviceCommandPriority.High:
                await _highPriorityQueue.Writer.WriteAsync(envelope, cancellationToken);
                break;
            case DeviceCommandPriority.Medium:
                await _mediumPriorityQueue.Writer.WriteAsync(envelope, cancellationToken);
                break;
            case DeviceCommandPriority.Low:
                await _lowPriorityQueue.Writer.WriteAsync(envelope, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unsupported command priority.");
        }
    }

    private async Task WaitForAnyQueueAsync(CancellationToken cancellationToken)
    {
        var highTask = _highPriorityQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
        var mediumTask = _mediumPriorityQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
        var lowTask = _lowPriorityQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
        await Task.WhenAny(highTask, mediumTask, lowTask);
    }

    private async Task<DeviceStatus> PublishConnectionStateAsync(
        ConnectionState state,
        string? portName,
        string? reason,
        CancellationToken cancellationToken,
        string? deviceId = null,
        string? firmwareVersion = null)
    {
        var current = stateStore.GetSnapshot().DeviceStatus.ConnectionState;
        if (!ConnectionStateMachine.IsTransitionAllowed(current, state))
        {
            logger.LogWarning("Unusual connection state transition: {From} -> {To}", current, state);
        }

        var snapshot = stateStore.UpdateDeviceStatus(previous => new DeviceStatus(
            ConnectionState: state,
            PortName: portName ?? previous.PortName,
            DeviceId: deviceId ?? previous.DeviceId,
            FirmwareVersion: firmwareVersion ?? previous.FirmwareVersion,
            LastSeenUtc: DateTimeOffset.UtcNow));

        await eventBus.PublishAsync(
            ProtocolNames.Events.ConnectionStateChanged,
            new ConnectionStateChangedEvent(
                snapshot.DeviceStatus.ConnectionState,
                snapshot.DeviceStatus.PortName,
                snapshot.DeviceStatus.DeviceId,
                snapshot.DeviceStatus.FirmwareVersion,
                snapshot.DeviceStatus.LastSeenUtc,
                reason),
            cancellationToken);

        return snapshot.DeviceStatus;
    }

    private async Task DisconnectInternalAsync(bool forceState, CancellationToken cancellationToken)
    {
        IDeviceLink? link;
        lock (_gate)
        {
            link = _activeLink;
            _activeLink = null;
        }

        if (link is not null)
        {
            link.MessageReceived -= OnDeviceMessageReceived;
            try
            {
                await link.DisconnectAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error while disconnecting device link.");
            }
            await link.DisposeAsync();
        }

        foreach (var pending in _pendingResponses.ToArray())
        {
            if (_pendingResponses.TryRemove(pending.Key, out var completion))
            {
                completion.TrySetException(new ProtocolException(ProtocolNames.ErrorCodes.NotConnected, "Device disconnected."));
            }
        }

        if (forceState)
        {
            await PublishConnectionStateAsync(ConnectionState.Disconnected, null, reason: null, cancellationToken);
        }
    }

    private async Task TryStartAutoReconnectAsync()
    {
        var settings = stateStore.GetSnapshot().Settings;
        if (!settings.AutoReconnectOnError)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_lastPortName))
        {
            return;
        }

        lock (_gate)
        {
            if (_autoReconnectTask is not null && !_autoReconnectTask.IsCompleted)
            {
                return;
            }

            _autoReconnectCts = new CancellationTokenSource();
            var token = _autoReconnectCts.Token;
            _autoReconnectTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(runtimeOptions.Value.AutoReconnectDelayMs, token);
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    var port = _lastPortName;
                    if (string.IsNullOrWhiteSpace(port))
                    {
                        break;
                    }

                    try
                    {
                        await PublishConnectionStateAsync(ConnectionState.Reconnecting, port, reason: null, CancellationToken.None);
                        await ConnectAsync(port, token);
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Auto-reconnect attempt failed for {Port}", port);
                    }
                }
            }, token);
        }
    }

    private async Task StopAutoReconnectAsync()
    {
        Task? reconnectTask;
        CancellationTokenSource? reconnectCts;
        lock (_gate)
        {
            reconnectTask = _autoReconnectTask;
            reconnectCts = _autoReconnectCts;
            _autoReconnectTask = null;
            _autoReconnectCts = null;
        }

        if (reconnectCts is null)
        {
            return;
        }

        await reconnectCts.CancelAsync();
        try
        {
            if (reconnectTask is not null)
            {
                await reconnectTask;
            }
        }
        catch (Exception)
        {
            // ignored
        }
        finally
        {
            reconnectCts.Dispose();
        }
    }

    private void OnDeviceMessageReceived(object? sender, ProtocolEnvelope envelope)
    {
        if (envelope.Type == ProtocolMessageType.Response && !string.IsNullOrWhiteSpace(envelope.Id))
        {
            if (_pendingResponses.TryRemove(envelope.Id, out var completion))
            {
                completion.TrySetResult(envelope);
            }
            return;
        }

        if (envelope.Type != ProtocolMessageType.Event)
        {
            return;
        }

        _ = HandleEventEnvelopeAsync(envelope);
    }

    private async Task HandleEventEnvelopeAsync(ProtocolEnvelope envelope)
    {
        try
        {
            switch (envelope.Name)
            {
                case ProtocolNames.DeviceMethods.DeviceHello:
                    {
                        var hello = ProtocolJson.DeserializePayload<DeviceHello>(envelope);
                        _helloWaiter?.TrySetResult(hello);
                        await PublishConnectionStateAsync(
                            ConnectionState.Connected,
                            GetActiveLink()?.PortName,
                            reason: null,
                            CancellationToken.None,
                            hello.DeviceId,
                            hello.FirmwareVersion);
                        break;
                    }
                case ProtocolNames.DeviceMethods.DeviceInputButton:
                    {
                        var buttonInput = ProtocolJson.DeserializePayload<DeviceButtonInput>(envelope);
                        ButtonInputReceived?.Invoke(this, buttonInput);
                        break;
                    }
                case ProtocolNames.DeviceMethods.DeviceInputEncoder:
                    {
                        var encoderInput = ProtocolJson.DeserializePayload<DeviceEncoderInput>(envelope);
                        EncoderInputReceived?.Invoke(this, encoderInput);
                        break;
                    }
                case ProtocolNames.DeviceMethods.DebugState:
                    {
                        var debugState = ProtocolJson.DeserializePayload<DebugState>(envelope);
                        DebugStateReceived?.Invoke(this, debugState);
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed handling device event {Name}", envelope.Name);
        }
    }

    private void FailPendingForEnvelope(ProtocolEnvelope envelope, Exception exception)
    {
        if (!string.IsNullOrWhiteSpace(envelope.Id) && _pendingResponses.TryRemove(envelope.Id, out var completion))
        {
            completion.TrySetException(exception);
        }
    }
}
