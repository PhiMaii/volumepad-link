using System.IO.Ports;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Device;

public sealed class DeviceService(
    IDeviceProtocolCodec codec,
    AgentStateStore stateStore,
    IEventHub eventHub,
    IOutboundMessageScheduler outboundScheduler,
    IReconnectPolicy reconnectPolicy,
    IOptions<AgentOptions> options,
    ILogger<DeviceService> logger,
    ILogger<SimulatedDeviceSession> simulatorLogger) : IDeviceService
{
    private readonly SemaphoreSlim _sync = new(1, 1);

    private SerialPort? _port;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _connectionCts;
    private Task? _readLoopTask;
    private Task? _writeLoopTask;
    private SimulatedDeviceSession? _simulator;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private bool _manualReconnectSuppressed;
    private int _reconnectAttempt;
    private string? _lastReconnectReason;
    private int _connectionGeneration;

    public event Func<DeviceInputEventDto, Task>? InputEventReceived;
    public event Func<DeviceStatusDto, Task>? DeviceStatusChanged;
    public event Func<DeviceCapabilitiesDto, Task>? CapabilitiesReceived;

    public Task<DeviceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(stateStore.GetDeviceStatus());
    }

    public Task<DeviceStatusDto> ConnectAsync(string? portName = null, CancellationToken cancellationToken = default)
    {
        return ConnectInternalAsync(portName, isExplicitRequest: true, cancellationToken);
    }

    public async Task<DeviceStatusDto> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _manualReconnectSuppressed = true;
            StopReconnectLoopUnsafe();
            await DisconnectUnsafeAsync("manual", allowReconnect: false, cancellationToken);
            return stateStore.GetDeviceStatus();
        }
        finally
        {
            _sync.Release();
        }
    }

    public Task<DeviceCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(stateStore.GetDeviceStatus().Capabilities);
    }

    public async Task ApplySettingsAsync(DeviceSettingsDto settings, CancellationToken cancellationToken = default)
    {
        stateStore.SetSettings(settings);
        await EnqueueControlMessageAsync("settings.apply", settings, OutboundPriority.High, cancellationToken);
    }

    public Task SendDisplayModelAsync(DisplayModelDto model, CancellationToken cancellationToken = default)
    {
        return EnqueueControlMessageAsync("display.render", model, OutboundPriority.Normal, cancellationToken);
    }

    public Task SendLedMeterAsync(LedMeterModelDto model, CancellationToken cancellationToken = default)
    {
        return EnqueueControlMessageAsync("leds.meter", model, OutboundPriority.Low, cancellationToken);
    }

    public Task SendRawMessageAsync(string type, object payload, CancellationToken cancellationToken = default)
    {
        return EnqueueControlMessageAsync(type, payload, ResolvePriority(type), cancellationToken);
    }

    private async Task<DeviceStatusDto> ConnectInternalAsync(string? portName, bool isExplicitRequest, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (isExplicitRequest)
            {
                _manualReconnectSuppressed = false;
                StopReconnectLoopUnsafe();
            }

            var current = stateStore.GetDeviceStatus();
            if (current.IsConnected)
            {
                return current;
            }

            var resolvedPort = ResolvePortName(portName, current.PortName);
            if (string.IsNullOrWhiteSpace(resolvedPort))
            {
                logger.LogWarning("No COM port available for device connection.");
                return stateStore.GetDeviceStatus();
            }

            if (IsSimulatorPort(resolvedPort))
            {
                return await ConnectSimulatorAsync(current, resolvedPort, cancellationToken);
            }

            var serialPort = new SerialPort(resolvedPort, options.Value.DeviceBaudRate)
            {
                NewLine = "\n",
                ReadTimeout = 500,
                WriteTimeout = 500
            };

            serialPort.Open();
            _port = serialPort;
            _reader = new StreamReader(serialPort.BaseStream);
            _writer = new StreamWriter(serialPort.BaseStream) { AutoFlush = true };
            _connectionCts = new CancellationTokenSource();

            var generation = Interlocked.Increment(ref _connectionGeneration);
            _readLoopTask = Task.Run(() => ReadLoopAsync(_connectionCts.Token), _connectionCts.Token);
            _writeLoopTask = Task.Run(() => WriteLoopAsync(_connectionCts.Token), _connectionCts.Token);

            var status = current with
            {
                IsConnected = true,
                PortName = resolvedPort,
                DeviceId = current.DeviceId ?? "volumepad",
                LastSeenUtc = DateTimeOffset.UtcNow
            };

            stateStore.SetDeviceStatus(status);
            await PublishStatusChangedAsync(status);
            await eventHub.PublishAsync(EventNames.DeviceConnected, new DeviceConnectedEvent(status), cancellationToken);

            if (status.Capabilities is not null)
            {
                await PublishCapabilitiesAsync(status.Capabilities, cancellationToken);
            }

            await EnqueueControlMessageAsync("hello", new { app = "VolumePadLink.Agent", version = "1.0" }, OutboundPriority.High, cancellationToken, generation);
            await ApplySettingsAsync(stateStore.GetSettings(), cancellationToken);

            return status;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<DeviceStatusDto> ConnectSimulatorAsync(DeviceStatusDto current, string simulatorPort, CancellationToken cancellationToken)
    {
        _connectionCts = new CancellationTokenSource();
        _simulator = new SimulatedDeviceSession(codec, HandleIncomingLineAsync, simulatorLogger);
        await _simulator.StartAsync(_connectionCts.Token);

        Interlocked.Increment(ref _connectionGeneration);

        var status = current with
        {
            IsConnected = true,
            PortName = simulatorPort,
            DeviceId = "volumepad-sim",
            FirmwareVersion = "sim-1.0",
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        stateStore.SetDeviceStatus(status);
        await PublishStatusChangedAsync(status);
        await eventHub.PublishAsync(EventNames.DeviceConnected, new DeviceConnectedEvent(status), cancellationToken);

        if (status.Capabilities is not null)
        {
            await PublishCapabilitiesAsync(status.Capabilities, cancellationToken);
        }

        await EnqueueControlMessageAsync("hello", new { app = "VolumePadLink.Agent", version = "1.0" }, OutboundPriority.High, cancellationToken);
        await ApplySettingsAsync(stateStore.GetSettings(), cancellationToken);

        logger.LogInformation("Connected to simulated device on port token {PortName}.", simulatorPort);
        return status;
    }

    private static bool IsSimulatorPort(string portName)
    {
        return portName.StartsWith("sim", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolvePortName(string? requestedPortName, string? previousPortName)
    {
        if (!string.IsNullOrWhiteSpace(requestedPortName))
        {
            return requestedPortName;
        }

        if (!string.IsNullOrWhiteSpace(previousPortName))
        {
            return previousPortName;
        }

        if (!string.IsNullOrWhiteSpace(options.Value.PreferredDevicePort))
        {
            return options.Value.PreferredDevicePort;
        }

        return SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_reader is null)
                {
                    break;
                }

                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    await DisconnectForErrorAsync("stream-closed", cancellationToken);
                    break;
                }

                await HandleIncomingLineAsync(line, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TimeoutException)
            {
                // Read timeout is expected; continue polling.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Device read loop failed.");
                await eventHub.PublishAsync(EventNames.DiagnosticsWarning, new DiagnosticsEvent("Device read loop failed", ex.Message), cancellationToken);
                await DisconnectForErrorAsync("read-failure", cancellationToken);
                break;
            }
        }
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            OutboundMessage outbound;

            try
            {
                outbound = await outboundScheduler.DequeueAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                if (outbound.Generation != Volatile.Read(ref _connectionGeneration))
                {
                    continue;
                }

                if (_writer is null)
                {
                    return;
                }

                await _writer.WriteLineAsync(outbound.Line);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Device write loop failed.");
                await eventHub.PublishAsync(EventNames.DiagnosticsWarning, new DiagnosticsEvent("Device write loop failed", ex.Message), cancellationToken);
                await DisconnectForErrorAsync("write-failure", cancellationToken);
                return;
            }
        }
    }

    private async Task HandleIncomingLineAsync(string line, CancellationToken cancellationToken)
    {
        if (!codec.TryParseEnvelope(line, out var envelope, out var error) || envelope is null)
        {
            await eventHub.PublishAsync(EventNames.DiagnosticsWarning, new DiagnosticsEvent("Invalid device message", error), cancellationToken);
            return;
        }

        var status = stateStore.GetDeviceStatus() with { LastSeenUtc = DateTimeOffset.UtcNow };
        stateStore.SetDeviceStatus(status);

        switch (envelope.Type)
        {
            case "hello":
                break;
            case "capabilities":
                await HandleCapabilitiesAsync(envelope.Payload, cancellationToken);
                break;
            case "input.event":
                await HandleInputEventAsync(envelope.Payload, cancellationToken);
                break;
            case "ack":
                logger.LogDebug("Device ack received. RequestId={RequestId}", envelope.RequestId);
                break;
            case "nack":
                await eventHub.PublishAsync(EventNames.DiagnosticsError, new DiagnosticsEvent("Device nack received", envelope.Payload.ToString()), cancellationToken);
                break;
            case "diag.log":
                await eventHub.PublishAsync(EventNames.DiagnosticsWarning, new DiagnosticsEvent("Device diagnostic", envelope.Payload.ToString()), cancellationToken);
                break;
            default:
                await eventHub.PublishAsync(EventNames.DiagnosticsWarning, new DiagnosticsEvent($"Unknown device message type: {envelope.Type}"), cancellationToken);
                break;
        }
    }

    private async Task HandleCapabilitiesAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var capabilities = new DeviceCapabilitiesDto(
            GetOrDefault(payload, "protocolVersion", "1"),
            GetOrDefault(payload, "supportsDisplay", true),
            GetOrDefault(payload, "supportsLedMeter", true),
            GetOrDefault(payload, "maxPacketSize", 256),
            GetOrDefault(payload, "maxFrameRateHz", 20));

        var status = stateStore.GetDeviceStatus() with
        {
            Capabilities = capabilities,
            FirmwareVersion = GetOrDefault(payload, "firmwareVersion", "unknown")
        };

        stateStore.SetDeviceStatus(status);
        await PublishStatusChangedAsync(status);
        await PublishCapabilitiesAsync(capabilities, cancellationToken);
    }

    private async Task PublishCapabilitiesAsync(DeviceCapabilitiesDto capabilities, CancellationToken cancellationToken)
    {
        if (CapabilitiesReceived is { } handlers)
        {
            foreach (Func<DeviceCapabilitiesDto, Task> handler in handlers.GetInvocationList())
            {
                await handler(capabilities);
            }
        }

        await eventHub.PublishAsync(EventNames.DeviceCapabilitiesReceived, new DeviceCapabilitiesReceivedEvent(capabilities), cancellationToken);
    }

    private async Task HandleInputEventAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var inputEvent = new DeviceInputEventDto(
            GetOrDefault(payload, "deviceId", "volumepad"),
            GetOrDefault(payload, "controlId", "unknown"),
            GetOrDefault(payload, "eventType", "unknown"),
            GetOrDefault(payload, "delta", 0),
            GetOrDefault(payload, "pressed", false),
            GetOrDefault(payload, "position", 0),
            DateTimeOffset.UtcNow);

        if (InputEventReceived is { } handlers)
        {
            foreach (Func<DeviceInputEventDto, Task> handler in handlers.GetInvocationList())
            {
                await handler(inputEvent);
            }
        }

        await eventHub.PublishAsync(EventNames.DeviceInputEvent, inputEvent, cancellationToken);
    }

    private async Task PublishStatusChangedAsync(DeviceStatusDto status)
    {
        if (DeviceStatusChanged is null)
        {
            return;
        }

        foreach (Func<DeviceStatusDto, Task> handler in DeviceStatusChanged.GetInvocationList())
        {
            await handler(status);
        }
    }

    private async Task EnqueueControlMessageAsync(
        string type,
        object? payload,
        OutboundPriority priority,
        CancellationToken cancellationToken,
        int? generationOverride = null)
    {
        if (!stateStore.GetDeviceStatus().IsConnected)
        {
            return;
        }

        var line = codec.Encode(type, payload, requestId: Guid.NewGuid().ToString("N"));

        if (_simulator is not null)
        {
            await _simulator.ProcessOutboundAsync(line, cancellationToken);
            return;
        }

        var generation = generationOverride ?? Volatile.Read(ref _connectionGeneration);
        await outboundScheduler.EnqueueAsync(new OutboundMessage(line, generation), priority, cancellationToken);
    }

    private async Task DisconnectForErrorAsync(string reason, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await DisconnectUnsafeAsync(reason, allowReconnect: true, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task DisconnectUnsafeAsync(string reason, bool allowReconnect, CancellationToken cancellationToken)
    {
        _connectionCts?.Cancel();

        if (_simulator is not null)
        {
            await _simulator.DisposeAsync();
            _simulator = null;
        }

        try
        {
            if (_readLoopTask is not null)
            {
                await _readLoopTask;
            }
        }
        catch
        {
            // Ignore read loop shutdown errors.
        }

        try
        {
            if (_writeLoopTask is not null)
            {
                await _writeLoopTask;
            }
        }
        catch
        {
            // Ignore write loop shutdown errors.
        }

        _reader?.Dispose();
        _writer?.Dispose();

        if (_port is not null)
        {
            try
            {
                _port.Close();
            }
            catch
            {
                // Ignore close errors.
            }

            _port.Dispose();
        }

        _reader = null;
        _writer = null;
        _port = null;
        _connectionCts?.Dispose();
        _connectionCts = null;
        _readLoopTask = null;
        _writeLoopTask = null;

        var previous = stateStore.GetDeviceStatus();
        if (!previous.IsConnected)
        {
            return;
        }

        var disconnected = previous with
        {
            IsConnected = false,
            LastSeenUtc = DateTimeOffset.UtcNow
        };
        stateStore.SetDeviceStatus(disconnected);

        await PublishStatusChangedAsync(disconnected);
        await eventHub.PublishAsync(EventNames.DeviceDisconnected, new DeviceDisconnectedEvent(reason), cancellationToken);

        if (allowReconnect && !_manualReconnectSuppressed && !string.IsNullOrWhiteSpace(disconnected.PortName))
        {
            StartReconnectLoopUnsafe(disconnected.PortName, reason);
        }
    }

    private void StartReconnectLoopUnsafe(string portName, string reason)
    {
        if (_reconnectTask is { IsCompleted: false })
        {
            return;
        }

        _lastReconnectReason = reason;
        _reconnectAttempt = 0;
        _reconnectCts = new CancellationTokenSource();
        var reconnectToken = _reconnectCts.Token;

        _ = eventHub.PublishAsync(
            EventNames.DiagnosticsWarning,
            new DiagnosticsEvent($"Device disconnected ({reason}). Starting reconnect loop for {portName}."),
            reconnectToken);

        _reconnectTask = Task.Run(() => ReconnectLoopAsync(portName, reconnectToken), reconnectToken);
    }

    private async Task ReconnectLoopAsync(string fallbackPortName, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_manualReconnectSuppressed)
            {
                return;
            }

            var status = stateStore.GetDeviceStatus();
            if (status.IsConnected)
            {
                return;
            }

            var portName = string.IsNullOrWhiteSpace(status.PortName) ? fallbackPortName : status.PortName;
            if (string.IsNullOrWhiteSpace(portName))
            {
                return;
            }

            _reconnectAttempt++;
            var delay = reconnectPolicy.GetDelay(_reconnectAttempt);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var connected = await ConnectInternalAsync(portName, isExplicitRequest: false, cancellationToken);
                if (connected.IsConnected)
                {
                    await eventHub.PublishAsync(
                        EventNames.DiagnosticsWarning,
                        new DiagnosticsEvent($"Reconnect succeeded after {_reconnectAttempt} attempt(s).", _lastReconnectReason),
                        cancellationToken);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reconnect attempt {Attempt} failed for {PortName}.", _reconnectAttempt, portName);
            }
        }
    }

    private void StopReconnectLoopUnsafe()
    {
        if (_reconnectCts is null)
        {
            return;
        }

        _reconnectCts.Cancel();
        _reconnectCts.Dispose();
        _reconnectCts = null;
        _reconnectTask = null;
        _reconnectAttempt = 0;
        _lastReconnectReason = null;
    }

    private static OutboundPriority ResolvePriority(string type)
    {
        if (type.StartsWith("leds.", StringComparison.OrdinalIgnoreCase))
        {
            return OutboundPriority.Low;
        }

        if (type.StartsWith("display.", StringComparison.OrdinalIgnoreCase))
        {
            return OutboundPriority.Normal;
        }

        return OutboundPriority.High;
    }

    private static T GetOrDefault<T>(JsonElement element, string propertyName, T defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(valueElement.GetRawText()) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }
}
