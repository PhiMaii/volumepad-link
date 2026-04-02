using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Core;
using VolumePadLink.Agent.Options;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Transport.Ipc;

public sealed class IpcServerService(
    IOptions<AgentRuntimeOptions> runtimeOptions,
    ICommandRouter commandRouter,
    EventBus eventBus,
    ILogger<IpcServerService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, IpcClientSession> _clients = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var broadcastTask = Task.Run(() => BroadcastEventsAsync(stoppingToken), stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(
                    runtimeOptions.Value.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await server.WaitForConnectionAsync(stoppingToken);
                    _ = Task.Run(() => HandleClientAsync(server, stoppingToken), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    await server.DisposeAsync();
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed accepting pipe client.");
                    await server.DisposeAsync();
                }
            }
        }
        finally
        {
            try
            {
                await broadcastTask;
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }
        _clients.Clear();
        await base.StopAsync(cancellationToken);
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid();
        var session = new IpcClientSession(stream, logger);
        _clients[clientId] = session;
        logger.LogInformation("IPC client connected ({ClientId}).", clientId);

        try
        {
            while (!cancellationToken.IsCancellationRequested && stream.IsConnected)
            {
                var line = await session.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                ProtocolEnvelope? request;
                try
                {
                    request = JsonSerializer.Deserialize<ProtocolEnvelope>(line, ProtocolJson.SerializerOptions);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Invalid IPC JSON received.");
                    continue;
                }

                if (request is null || request.Type != ProtocolMessageType.Request)
                {
                    continue;
                }

                var response = await commandRouter.HandleAsync(request, cancellationToken);
                await session.SendAsync(response, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IPC client handler failed for {ClientId}.", clientId);
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            await session.DisposeAsync();
            logger.LogInformation("IPC client disconnected ({ClientId}).", clientId);
        }
    }

    private async Task BroadcastEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (var envelope in eventBus.ReadAllAsync(cancellationToken))
        {
            if (_clients.IsEmpty)
            {
                continue;
            }

            foreach (var session in _clients.Values)
            {
                try
                {
                    await session.SendAsync(envelope, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed broadcasting event to one IPC client.");
                }
            }
        }
    }

    private sealed class IpcClientSession : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _stream;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly ILogger _logger;

        public IpcClientSession(NamedPipeServerStream stream, ILogger logger)
        {
            _stream = stream;
            _reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            _writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            _logger = logger;
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            return await _reader.ReadLineAsync(cancellationToken);
        }

        public async Task SendAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(envelope, ProtocolJson.SerializerOptions);

            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                if (!_stream.IsConnected)
                {
                    return;
                }

                await _writer.WriteLineAsync(payload);
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Pipe write failed.");
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _writeGate.Dispose();
            _reader.Dispose();
            _writer.Dispose();
            await _stream.DisposeAsync();
        }
    }
}
