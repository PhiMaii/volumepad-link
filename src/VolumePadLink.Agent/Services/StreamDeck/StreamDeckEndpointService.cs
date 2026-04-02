using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Options;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Services.StreamDeck;

public sealed class StreamDeckEndpointService(
    IOptions<AgentRuntimeOptions> runtimeOptions,
    IStreamDeckStateProvider stateProvider,
    IStreamDeckCommandService commandService,
    StreamDeckTrafficMonitor trafficMonitor,
    ILogger<StreamDeckEndpointService> logger) : BackgroundService
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<Guid, StreamDeckWebSocketSession> _stateSockets = new();
    private readonly TimeSpan _statePollInterval = TimeSpan.FromMilliseconds(Math.Max(25, runtimeOptions.Value.StreamDeckStatePollMs));
    private readonly string _prefix = BuildPrefix(runtimeOptions.Value.StreamDeckBindAddress, runtimeOptions.Value.StreamDeckPort);
    private Task? _stateBroadcastTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!runtimeOptions.Value.StreamDeckEndpointEnabled)
        {
            logger.LogInformation("Stream Deck endpoint is disabled by runtime options.");
            return;
        }

        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        logger.LogInformation("Stream Deck endpoint listening on {Prefix}", _prefix);

        _stateBroadcastTask = Task.Run(() => BroadcastStateChangesAsync(stoppingToken), stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Stream Deck endpoint failed accepting connection.");
                    continue;
                }

                _ = Task.Run(() => HandleContextSafelyAsync(context, stoppingToken), stoppingToken);
            }
        }
        finally
        {
            _listener.Close();

            foreach (var pair in _stateSockets.ToArray())
            {
                if (_stateSockets.TryRemove(pair.Key, out var session))
                {
                    await session.DisposeAsync();
                }
            }

            if (_stateBroadcastTask is not null)
            {
                try
                {
                    await _stateBroadcastTask;
                }
                catch (Exception)
                {
                    // ignored during shutdown
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task HandleContextSafelyAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await HandleContextAsync(context, cancellationToken);
        }
        catch (ProtocolException ex)
        {
            await WriteErrorAsync(context, ex.Code, ex.Message, cancellationToken);
        }
        catch (JsonException ex)
        {
            await WriteErrorAsync(context, ProtocolNames.ErrorCodes.InvalidPayload, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unhandled Stream Deck request failure.");
            await WriteErrorAsync(context, ProtocolNames.ErrorCodes.InternalError, ex.Message, cancellationToken);
        }
        finally
        {
            try
            {
                if (context.Response.StatusCode != (int)HttpStatusCode.SwitchingProtocols)
                {
                    context.Response.OutputStream.Close();
                    context.Response.Close();
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var method = request.HttpMethod.ToUpperInvariant();
        var path = NormalizePath(request.Url?.AbsolutePath);

        JsonElement payload = ProtocolJson.EmptyObject;
        if (request.HasEntityBody)
        {
            payload = await ReadJsonPayloadAsync(request);
        }

        trafficMonitor.Record(
            direction: "in",
            transport: "http",
            name: $"{method} {path}",
            payload: new
            {
                query = request.Url?.Query ?? string.Empty,
                payload,
            });

        switch (method, path)
        {
            case ("GET", "/"):
                await WriteJsonAsync(context, 200, new
                {
                    ok = true,
                    service = "VolumePadLink.StreamDeckEndpoint",
                    streamDeckState = "/api/v1/streamdeck/state",
                    streamDeckWs = "/api/v1/streamdeck/ws",
                    debugTraffic = "/api/v1/debug/streamdeck/traffic",
                }, cancellationToken);
                break;

            case ("GET", "/api/v1/streamdeck/state"):
                await WriteJsonAsync(context, 200, stateProvider.GetStateSnapshot(), cancellationToken);
                break;

            case ("POST", "/api/v1/streamdeck/actions/master/mute/toggle"):
                await WriteJsonAsync(context, 200, await commandService.ToggleMuteAsync(cancellationToken), cancellationToken);
                break;

            case ("POST", "/api/v1/streamdeck/actions/master/volume/increase"):
                await WriteJsonAsync(context, 200, await commandService.AdjustVolumeByStepAsync(ReadVolumeStep(payload), cancellationToken), cancellationToken);
                break;

            case ("POST", "/api/v1/streamdeck/actions/master/volume/decrease"):
                await WriteJsonAsync(context, 200, await commandService.AdjustVolumeByStepAsync(-ReadVolumeStep(payload), cancellationToken), cancellationToken);
                break;

            case ("GET", "/api/v1/streamdeck/settings"):
                await WriteJsonAsync(context, 200, commandService.GetSettingsSnapshot(), cancellationToken);
                break;

            case ("POST", "/api/v1/streamdeck/settings/update"):
                await WriteJsonAsync(context, 200, await commandService.UpdateSettingsAsync(ReadSettingsPatch(payload), cancellationToken), cancellationToken);
                break;

            case ("GET", "/api/v1/streamdeck/ws"):
                await HandleStateWebSocketAsync(context, cancellationToken);
                break;

            case ("GET", "/api/v1/debug/streamdeck/traffic"):
                await HandleTrafficSnapshotAsync(context, cancellationToken);
                break;

            case ("DELETE", "/api/v1/debug/streamdeck/traffic"):
            case ("POST", "/api/v1/debug/streamdeck/traffic/clear"):
                trafficMonitor.Clear();
                await WriteJsonAsync(context, 200, new { ok = true }, cancellationToken);
                break;

            case ("GET", "/api/v1/debug/streamdeck/endpoints"):
                await WriteJsonAsync(context, 200, new
                {
                    endpoints = new[]
                    {
                        "GET /api/v1/streamdeck/state",
                        "POST /api/v1/streamdeck/actions/master/mute/toggle",
                        "POST /api/v1/streamdeck/actions/master/volume/increase",
                        "POST /api/v1/streamdeck/actions/master/volume/decrease",
                        "GET /api/v1/streamdeck/settings",
                        "POST /api/v1/streamdeck/settings/update",
                        "GET /api/v1/streamdeck/ws",
                        "GET /api/v1/debug/streamdeck/traffic",
                        "DELETE /api/v1/debug/streamdeck/traffic",
                    },
                }, cancellationToken);
                break;

            default:
                throw new ProtocolException(ProtocolNames.ErrorCodes.UnknownMethod, $"Unsupported endpoint '{method} {path}'.");
        }
    }

    private async Task HandleTrafficSnapshotAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var sinceSeqRaw = request.QueryString["sinceSeq"];
        var limitRaw = request.QueryString["limit"];

        var sinceSeq = 0L;
        if (!string.IsNullOrWhiteSpace(sinceSeqRaw) && !long.TryParse(sinceSeqRaw, out sinceSeq))
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "sinceSeq must be a valid integer.");
        }

        var limit = 200;
        if (!string.IsNullOrWhiteSpace(limitRaw) && !int.TryParse(limitRaw, out limit))
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "limit must be a valid integer.");
        }

        await WriteJsonAsync(context, 200, trafficMonitor.GetSnapshot(sinceSeq, limit), cancellationToken);
    }

    private async Task HandleStateWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "WebSocket upgrade required.");
        }

        HttpListenerWebSocketContext wsContext;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
        }
        catch (Exception ex)
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InternalError, $"Failed to accept WebSocket: {ex.Message}");
        }

        var sessionId = Guid.NewGuid();
        var socket = new StreamDeckWebSocketSession(wsContext.WebSocket);
        _stateSockets[sessionId] = socket;
        trafficMonitor.Record("in", "ws", "state.connect", new { sessionId });

        try
        {
            await SendStateEventAsync(socket, "state.snapshot", stateProvider.GetStateSnapshot(), cancellationToken);
            await ReceiveUntilClosedAsync(socket.Socket, sessionId, cancellationToken);
        }
        finally
        {
            if (_stateSockets.TryRemove(sessionId, out var removed))
            {
                await removed.DisposeAsync();
            }

            trafficMonitor.Record("out", "ws", "state.disconnect", new { sessionId });
        }
    }

    private async Task BroadcastStateChangesAsync(CancellationToken cancellationToken)
    {
        StreamDeckState? last = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = stateProvider.GetStateSnapshot();
            if (!IsSameState(last, snapshot))
            {
                last = CloneState(snapshot);
                await BroadcastStateEventAsync("state.changed", snapshot, cancellationToken);
            }

            try
            {
                await Task.Delay(_statePollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task BroadcastStateEventAsync(string eventType, StreamDeckState state, CancellationToken cancellationToken)
    {
        if (_stateSockets.IsEmpty)
        {
            return;
        }

        foreach (var pair in _stateSockets.ToArray())
        {
            try
            {
                await SendStateEventAsync(pair.Value, eventType, state, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed pushing Stream Deck WebSocket event.");
                if (_stateSockets.TryRemove(pair.Key, out var removed))
                {
                    await removed.DisposeAsync();
                }
            }
        }
    }

    private async Task SendStateEventAsync(StreamDeckWebSocketSession socket, string eventType, StreamDeckState state, CancellationToken cancellationToken)
    {
        var envelope = new StreamDeckEventEnvelope
        {
            Type = eventType,
            Payload = new StreamDeckStateEventPayload
            {
                State = CloneState(state),
            },
            UtcNow = DateTimeOffset.UtcNow,
        };

        await socket.SendJsonAsync(envelope, cancellationToken);
        trafficMonitor.Record("out", "ws", eventType, envelope);
    }

    private async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object payload, CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, ProtocolJson.SerializerOptions);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);

        var path = NormalizePath(context.Request.Url?.AbsolutePath);
        var method = context.Request.HttpMethod.ToUpperInvariant();
        trafficMonitor.Record("out", "http", $"{method} {path}", payload, statusCode);
    }

    private async Task WriteErrorAsync(HttpListenerContext context, string code, string message, CancellationToken cancellationToken)
    {
        var statusCode = code switch
        {
            ProtocolNames.ErrorCodes.InvalidPayload => 400,
            ProtocolNames.ErrorCodes.OutOfRange => 400,
            ProtocolNames.ErrorCodes.UnknownMethod => 404,
            ProtocolNames.ErrorCodes.NotConnected => 409,
            ProtocolNames.ErrorCodes.Timeout => 504,
            _ => 500,
        };

        await WriteJsonAsync(context, statusCode, new
        {
            ok = false,
            error = new ProtocolError(code, message),
        }, cancellationToken);
    }

    private static async Task<JsonElement> ReadJsonPayloadAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        var raw = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return ProtocolJson.EmptyObject;
        }

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private async Task ReceiveUntilClosedAsync(WebSocket socket, Guid sessionId, CancellationToken cancellationToken)
    {
        var buffer = new byte[2048];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                trafficMonitor.Record("in", "ws", "state.close", new { sessionId });
                break;
            }

            trafficMonitor.Record("in", "ws", "state.message", new
            {
                sessionId,
                messageType = result.MessageType.ToString(),
                count = result.Count,
                result.EndOfMessage,
            });
        }
    }

    private static StreamDeckSettingsPatch ReadSettingsPatch(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("settings", out var nested))
        {
            return nested.Deserialize<StreamDeckSettingsPatch>(ProtocolJson.SerializerOptions)
                ?? new StreamDeckSettingsPatch();
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "settings payload must be a JSON object.");
        }

        return payload.Deserialize<StreamDeckSettingsPatch>(ProtocolJson.SerializerOptions)
            ?? new StreamDeckSettingsPatch();
    }

    private static double ReadVolumeStep(JsonElement payload)
    {
        StreamDeckVolumeStepRequest? request;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("step", out _))
        {
            request = payload.Deserialize<StreamDeckVolumeStepRequest>(ProtocolJson.SerializerOptions);
        }
        else if (payload.ValueKind == JsonValueKind.Undefined || payload.ValueKind == JsonValueKind.Null || payload.ValueKind == JsonValueKind.Object)
        {
            request = new StreamDeckVolumeStepRequest();
        }
        else
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "step payload must be an object.");
        }

        if (request is null || !double.IsFinite(request.Step))
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.InvalidPayload, "step must be a finite number.");
        }

        if (request.Step < 0.001 || request.Step > 0.20)
        {
            throw new ProtocolException(ProtocolNames.ErrorCodes.OutOfRange, "step must be between 0.001 and 0.20.");
        }

        return request.Step;
    }

    private static StreamDeckState CloneState(StreamDeckState source)
    {
        return new StreamDeckState
        {
            Master = new StreamDeckMasterState
            {
                Volume = source.Master.Volume,
                Muted = source.Master.Muted,
            },
            DeviceConnection = new StreamDeckDeviceConnection
            {
                State = source.DeviceConnection.State,
                PortName = source.DeviceConnection.PortName,
            },
            CapturedAtUtc = source.CapturedAtUtc,
        };
    }

    private static bool IsSameState(StreamDeckState? left, StreamDeckState right)
    {
        if (left is null)
        {
            return false;
        }

        return Math.Abs(left.Master.Volume - right.Master.Volume) < 0.00001
               && left.Master.Muted == right.Master.Muted
               && left.DeviceConnection.State == right.DeviceConnection.State
               && string.Equals(left.DeviceConnection.PortName, right.DeviceConnection.PortName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "/";
        }

        if (rawPath.Length > 1 && rawPath.EndsWith('/'))
        {
            return rawPath.TrimEnd('/');
        }

        return rawPath;
    }

    private static string BuildPrefix(string bindAddress, int port)
    {
        var host = string.IsNullOrWhiteSpace(bindAddress) ? "127.0.0.1" : bindAddress.Trim();
        var safePort = Math.Clamp(port, 1025, 65535);
        return $"http://{host}:{safePort}/";
    }

    private sealed class StreamDeckWebSocketSession(WebSocket socket) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        public WebSocket Socket { get; } = socket;

        public async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, ProtocolJson.SerializerOptions);

            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                if (Socket.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("WebSocket is not open.");
                }

                await Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _sendGate.Dispose();

            try
            {
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
            catch (Exception)
            {
                // ignored during disposal
            }

            Socket.Dispose();
        }
    }
}
