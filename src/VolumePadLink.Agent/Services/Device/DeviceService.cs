using System.IO.Ports;
using System.Text.Json;
using System.Threading.Channels;
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
    IOptions<AgentOptions> options,
    ILogger<DeviceService> logger) : IDeviceService
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Channel<string> _outbound = Channel.CreateUnbounded<string>();

    private SerialPort? _port;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _connectionCts;
    private Task? _readLoopTask;
    private Task? _writeLoopTask;

    public event Func<DeviceInputEventDto, Task>? InputEventReceived;
    public event Func<DeviceStatusDto, Task>? DeviceStatusChanged;
    public event Func<DeviceCapabilitiesDto, Task>? CapabilitiesReceived;

    public Task<DeviceStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(stateStore.GetDeviceStatus());
    }

    public async Task<DeviceStatusDto> ConnectAsync(string? portName = null, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
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
            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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

            await EnqueueControlMessageAsync("hello", new { app = "VolumePadLink.Agent", version = "1.0" }, cancellationToken);
            await ApplySettingsAsync(stateStore.GetSettings(), cancellationToken);

            return status;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<DeviceStatusDto> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await DisconnectUnsafeAsync("manual", cancellationToken);
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
        await EnqueueControlMessageAsync("settings.apply", settings, cancellationToken);
    }

    public Task SendDisplayModelAsync(DisplayModelDto model, CancellationToken cancellationToken = default)
    {
        return EnqueueControlMessageAsync("display.render", model, cancellationToken);
    }

    public Task SendLedMeterAsync(LedMeterModelDto model, CancellationToken cancellationToken = default)
    {
        return EnqueueControlMessageAsync("leds.meter", model, cancellationToken);
    }

    public Task SendRawMessageAsync(string type, object payload, CancellationToken cancellationToken = default)
    {
        return EnqueueControlMessageAsync(type, payload, cancellationToken);
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
                    await DisconnectForErrorAsync("Device stream closed", cancellationToken);
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
        while (await _outbound.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_outbound.Reader.TryRead(out var line))
            {
                try
                {
                    if (_writer is null)
                    {
                        return;
                    }

                    await _writer.WriteLineAsync(line);
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
                await eventHub.PublishAsync(EventNames.DiagnosticsWarning, new DiagnosticsEvent("Device ack received", envelope.RequestId), cancellationToken);
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

    private async Task EnqueueControlMessageAsync(string type, object? payload, CancellationToken cancellationToken)
    {
        if (!stateStore.GetDeviceStatus().IsConnected)
        {
            return;
        }

        var line = codec.Encode(type, payload, requestId: Guid.NewGuid().ToString("N"));
        await _outbound.Writer.WriteAsync(line, cancellationToken);
    }

    private async Task DisconnectForErrorAsync(string reason, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await DisconnectUnsafeAsync(reason, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task DisconnectUnsafeAsync(string reason, CancellationToken cancellationToken)
    {
        _connectionCts?.Cancel();

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
            Capabilities = previous.Capabilities,
            LastSeenUtc = DateTimeOffset.UtcNow
        };
        stateStore.SetDeviceStatus(disconnected);

        await PublishStatusChangedAsync(disconnected);
        await eventHub.PublishAsync(EventNames.DeviceDisconnected, new DeviceDisconnectedEvent(reason), cancellationToken);
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

