using System.Collections.Concurrent;
using System.Text.Json;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Transport.Device;

public sealed class SimulatedDeviceLink(string simulatorPortName, ILogger<SimulatedDeviceLink> logger) : IDeviceLink
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, long> _streamSequences = new(StringComparer.OrdinalIgnoreCase);

    private bool _connected;
    private string? _portName;
    private DebugState _debugState = new();
    private DebugTuning _debugTuning = new();
    private bool _debugStreamEnabled;
    private int _debugStreamIntervalMs = 150;
    private CancellationTokenSource? _debugStreamCts;
    private Task? _debugStreamTask;

    public event EventHandler<ProtocolEnvelope>? MessageReceived;

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _connected;
            }
        }
    }

    public string? PortName
    {
        get
        {
            lock (_gate)
            {
                return _portName;
            }
        }
    }

    public Task ConnectAsync(string portName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _connected = true;
            _portName = string.IsNullOrWhiteSpace(portName) ? simulatorPortName : portName;
            _debugState = new DebugState
            {
                DeviceId = "volumepad-001",
                Source = "firmware",
                UptimeMs = 0,
                HapticsReady = true,
                Position = 42,
                DetentCount = 24,
                DetentStrength = 0.65,
                SnapStrength = 0.40,
            };
        }

        logger.LogInformation("Simulator connected on {Port}", PortName);
        EmitEvent(ProtocolNames.DeviceMethods.DeviceHello, new DeviceHello
        {
            DeviceId = _debugState.DeviceId,
            FirmwareVersion = "2.0.0",
            RingLedCount = 27,
            ButtonLedCount = 3,
        });
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? streamCts;
        Task? streamTask;

        lock (_gate)
        {
            _connected = false;
            _portName = null;
            _streamSequences.Clear();

            streamCts = _debugStreamCts;
            streamTask = _debugStreamTask;
            _debugStreamCts = null;
            _debugStreamTask = null;
            _debugStreamEnabled = false;
        }

        if (streamCts is not null)
        {
            await streamCts.CancelAsync();
            try
            {
                if (streamTask is not null)
                {
                    await streamTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
            catch (Exception)
            {
                // ignored on shutdown
            }
            streamCts.Dispose();
        }
    }

    public Task SendAsync(ProtocolEnvelope message, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Simulator is not connected.");
        }

        if (message.Type != ProtocolMessageType.Request)
        {
            return Task.CompletedTask;
        }

        switch (message.Name)
        {
            case ProtocolNames.DeviceMethods.DeviceApplySettings:
            case ProtocolNames.DeviceMethods.DeviceMeterFrame:
            case ProtocolNames.DeviceMethods.DeviceRingSetLed:
            case ProtocolNames.DeviceMethods.DeviceRingMuteOverride:
            case ProtocolNames.DeviceMethods.DeviceButtonLedsSet:
                EmitAck(message, ProtocolJson.EmptyObject);
                break;

            case ProtocolNames.DeviceMethods.DeviceRingStreamBegin:
                {
                    var begin = ProtocolJson.DeserializePayload<DeviceRingStreamBegin>(message);
                    _streamSequences[begin.StreamId] = -1;
                    EmitAck(message, ProtocolJson.EmptyObject);
                    break;
                }

            case ProtocolNames.DeviceMethods.DeviceRingStreamFrame:
                {
                    var frame = ProtocolJson.DeserializePayload<DeviceRingStreamFrame>(message);
                    _streamSequences.AddOrUpdate(frame.StreamId, frame.Seq, (_, previous) => Math.Max(previous, frame.Seq));
                    EmitAck(message, ProtocolJson.EmptyObject);
                    break;
                }

            case ProtocolNames.DeviceMethods.DeviceRingStreamEnd:
                {
                    var end = ProtocolJson.DeserializePayload<DeviceRingStreamEnd>(message);
                    _streamSequences.TryRemove(end.StreamId, out _);
                    EmitAck(message, ProtocolJson.EmptyObject);
                    break;
                }

            case ProtocolNames.DeviceMethods.DebugGetState:
                EmitAck(message, ProtocolJson.ToElement(_debugState));
                break;

            case ProtocolNames.DeviceMethods.DebugApplyTuning:
                ApplyDebugTuning(message);
                EmitAck(message, ProtocolJson.EmptyObject);
                EmitEvent(ProtocolNames.DeviceMethods.DebugState, _debugState);
                break;

            case ProtocolNames.DeviceMethods.DebugSetStream:
                ConfigureDebugStream(message, cancellationToken);
                EmitAck(message, ProtocolJson.EmptyObject);
                break;

            default:
                EmitError(message, ProtocolNames.ErrorCodes.UnknownMethod, $"Unsupported simulator method '{message.Name}'.");
                break;
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
    }

    private void ApplyDebugTuning(ProtocolEnvelope message)
    {
        var tuning = ProtocolJson.DeserializePayload<DebugTuning>(message);
        _debugTuning = tuning;
        _debugState.DetentStrength = Math.Clamp(tuning.DetentStrengthMaxVPerRad / 3.0, 0.0, 1.0);
        _debugState.SnapStrength = Math.Clamp(tuning.SnapStrengthMaxVPerRad / 3.0, 0.0, 1.0);
    }

    private void ConfigureDebugStream(ProtocolEnvelope message, CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.DeserializePayload<DebugSetStreamRequest>(message);
        _debugStreamEnabled = payload.Enabled;
        _debugStreamIntervalMs = Math.Max(30, payload.IntervalMs);

        if (!_debugStreamEnabled)
        {
            StopDebugStream();
            return;
        }

        StartDebugStream(cancellationToken);
    }

    private void StartDebugStream(CancellationToken cancellationToken)
    {
        StopDebugStream();

        _debugStreamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _debugStreamCts.Token;
        _debugStreamTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_debugStreamIntervalMs, token);
                    _debugState.UptimeMs += _debugStreamIntervalMs;
                    EmitEvent(ProtocolNames.DeviceMethods.DebugState, _debugState);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private void StopDebugStream()
    {
        var cts = _debugStreamCts;
        var task = _debugStreamTask;

        _debugStreamCts = null;
        _debugStreamTask = null;
        if (cts is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await cts.CancelAsync();
            try
            {
                if (task is not null)
                {
                    await task;
                }
            }
            catch (Exception)
            {
                // ignored
            }
            finally
            {
                cts.Dispose();
            }
        });
    }

    private void EmitAck(ProtocolEnvelope request, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            return;
        }

        EmitEnvelope(new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Response,
            V = ProtocolConstants.Version,
            Id = request.Id,
            Name = request.Name,
            Ok = true,
            Payload = payload,
        });
    }

    private void EmitError(ProtocolEnvelope request, string errorCode, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            return;
        }

        EmitEnvelope(new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Response,
            V = ProtocolConstants.Version,
            Id = request.Id,
            Name = request.Name,
            Ok = false,
            Error = new ProtocolError(errorCode, errorMessage),
            Payload = ProtocolJson.EmptyObject,
        });
    }

    private void EmitEvent(string name, object payload)
    {
        EmitEnvelope(new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Event,
            Name = name,
            TsUtc = DateTimeOffset.UtcNow,
            Payload = ProtocolJson.ToElement(payload),
        });
    }

    private void EmitEnvelope(ProtocolEnvelope envelope)
    {
        try
        {
            MessageReceived?.Invoke(this, envelope);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Simulator event dispatch failed for {Name}", envelope.Name);
        }
    }
}
