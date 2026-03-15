using System.Text.Json;
using VolumePadLink.Agent.Services.Interfaces;

namespace VolumePadLink.Agent.Services.Device;

public sealed class SimulatedDeviceSession(
    IDeviceProtocolCodec codec,
    Func<string, CancellationToken, Task> inboundLineSink,
    ILogger<SimulatedDeviceSession> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _inputLoopTask;
    private int _position;
    private int _direction = 1;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _inputLoopTask = Task.Run(() => InputLoopAsync(_cts.Token), _cts.Token);

        await EmitAsync("hello", new
        {
            deviceId = "volumepad-sim",
            firmwareVersion = "sim-1.0"
        }, null, cancellationToken);

        await EmitAsync("capabilities", new
        {
            protocolVersion = "1",
            firmwareVersion = "sim-1.0",
            supportsDisplay = true,
            supportsLedMeter = true,
            maxPacketSize = 1024,
            maxFrameRateHz = 60
        }, null, cancellationToken);
    }

    public async Task ProcessOutboundAsync(string line, CancellationToken cancellationToken)
    {
        if (!codec.TryParseEnvelope(line, out var envelope, out var error) || envelope is null)
        {
            await EmitAsync("diag.log", new { level = "warning", message = $"Simulator parse error: {error}" }, null, cancellationToken);
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            switch (envelope.Type)
            {
                case "hello":
                    await EmitAsync("ack", new { ok = true, type = envelope.Type }, envelope.RequestId, cancellationToken);
                    break;
                case "settings.apply":
                    await EmitSettingsAckAsync(envelope.Payload, envelope.RequestId, cancellationToken);
                    break;
                case "display.render":
                case "display.beginFrame":
                case "display.rect":
                case "display.endFrame":
                case "display.fullFrame":
                case "leds.meter":
                case "device.ping":
                    await EmitAsync("ack", new { ok = true, type = envelope.Type }, envelope.RequestId, cancellationToken);
                    break;
                default:
                    await EmitAsync("nack", new { ok = false, type = envelope.Type, reason = "unsupported-type" }, envelope.RequestId, cancellationToken);
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }

        if (_inputLoopTask is not null)
        {
            try
            {
                await _inputLoopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Simulator input loop stopped with exception.");
            }
        }

        _cts?.Dispose();
        _gate.Dispose();
    }

    private async Task InputLoopAsync(CancellationToken cancellationToken)
    {
        var step = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);

            _position += _direction;
            if (Math.Abs(_position) >= 20)
            {
                _direction *= -1;
            }

            await EmitAsync("input.event", new
            {
                deviceId = "volumepad-sim",
                controlId = "encoder-main",
                eventType = "rotate",
                delta = _direction,
                position = _position,
                pressed = false
            }, null, cancellationToken);

            step++;
            if (step % 5 == 0)
            {
                await EmitAsync("input.event", new
                {
                    deviceId = "volumepad-sim",
                    controlId = "button-1",
                    eventType = "press",
                    delta = 0,
                    position = _position,
                    pressed = true
                }, null, cancellationToken);
            }
        }
    }

    private async Task EmitSettingsAckAsync(JsonElement payload, string? requestId, CancellationToken cancellationToken)
    {
        var detentCount = GetOrDefault(payload, "detentCount", 24);
        var detentStrength = Math.Clamp(GetOrDefault(payload, "detentStrength", 0.65f), 0f, 1f);
        var snapStrength = Math.Clamp(GetOrDefault(payload, "snapStrength", 0.4f), 0f, 1f);
        var ledBrightness = Math.Clamp(GetOrDefault(payload, "ledBrightness", 0.8f), 0f, 1f);
        var displayBrightness = Math.Clamp(GetOrDefault(payload, "displayBrightness", 0.8f), 0f, 1f);
        var encoderInvert = GetOrDefault(payload, "encoderInvert", false);
        var buttonLongPressMs = Math.Clamp(GetOrDefault(payload, "buttonLongPressMs", 450), 100, 5000);

        await EmitAsync("ack", new
        {
            ok = true,
            type = "settings.apply",
            effective = new
            {
                detentCount,
                detentStrength,
                snapStrength,
                ledBrightness,
                displayBrightness,
                encoderInvert,
                buttonLongPressMs
            }
        }, requestId, cancellationToken);
    }

    private async Task EmitAsync(string type, object payload, string? requestId, CancellationToken cancellationToken)
    {
        var line = codec.Encode(type, payload, requestId);
        await inboundLineSink(line, cancellationToken);
    }

    private static T GetOrDefault<T>(JsonElement payload, string propertyName, T defaultValue)
    {
        if (!payload.TryGetProperty(propertyName, out var valueElement))
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
