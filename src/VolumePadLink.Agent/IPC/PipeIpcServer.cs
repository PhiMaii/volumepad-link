using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Configuration;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Contracts.Abstractions;

namespace VolumePadLink.Agent.IPC;

public sealed class PipeIpcServer(
    CommandRouter commandRouter,
    IEventHub eventHub,
    IOptions<AgentOptions> options,
    ILogger<PipeIpcServer> logger) : BackgroundService, IIpcServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<int, EventClientConnection> _eventClients = new();
    private int _eventClientId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        eventHub.EventPublished += BroadcastEventAsync;

        var commandLoop = AcceptCommandClientsLoopAsync(stoppingToken);
        var eventLoop = AcceptEventClientsLoopAsync(stoppingToken);

        await Task.WhenAll(commandLoop, eventLoop);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        eventHub.EventPublished -= BroadcastEventAsync;

        foreach (var connection in _eventClients.Values)
        {
            connection.Dispose();
        }

        _eventClients.Clear();
        return base.StopAsync(cancellationToken);
    }

    private async Task AcceptCommandClientsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                options.Value.CommandPipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                _ = Task.Run(() => HandleCommandClientAsync(server, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server.Dispose();
                logger.LogWarning(ex, "Command pipe accept failed.");
            }
        }
    }

    private async Task AcceptEventClientsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                options.Value.EventPipeName,
                PipeDirection.Out,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                _ = Task.Run(() => RegisterEventClientAsync(server, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server.Dispose();
                logger.LogWarning(ex, "Event pipe accept failed.");
            }
        }
    }

    private async Task HandleCommandClientAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        await using var stream = server;
        using var reader = new StreamReader(stream);
        using var writer = new StreamWriter(stream) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested && stream.IsConnected)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Reading command pipe failed.");
                break;
            }

            if (line is null)
            {
                break;
            }

            IpcMessage response;
            try
            {
                var command = JsonSerializer.Deserialize<IpcMessage>(line, JsonOptions);
                if (command is null)
                {
                    response = CreateErrorResponse(null, "Unknown", "Invalid command payload.");
                }
                else if (!string.Equals(command.Type, IpcMessageKinds.Command, StringComparison.OrdinalIgnoreCase))
                {
                    response = CreateErrorResponse(command.Id, command.Name, "Message type must be 'command'.");
                }
                else
                {
                    response = await commandRouter.HandleAsync(command, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Command processing failed.");
                response = CreateErrorResponse(null, "Unknown", ex.Message);
            }

            var responseLine = JsonSerializer.Serialize(response, JsonOptions);
            try
            {
                await writer.WriteLineAsync(responseLine);
            }
            catch
            {
                break;
            }
        }
    }

    private Task RegisterEventClientAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _eventClientId);
        var connection = new EventClientConnection(server);
        _eventClients[id] = connection;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && server.IsConnected)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during shutdown.
            }
            finally
            {
                RemoveEventClient(id);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private async Task BroadcastEventAsync(IpcMessage message)
    {
        var payload = JsonSerializer.Serialize(message, JsonOptions);
        var disconnected = new List<int>();

        foreach (var entry in _eventClients)
        {
            var connection = entry.Value;
            await connection.Sync.WaitAsync();
            try
            {
                if (!connection.Stream.IsConnected)
                {
                    disconnected.Add(entry.Key);
                    continue;
                }

                await connection.Writer.WriteLineAsync(payload);
            }
            catch
            {
                disconnected.Add(entry.Key);
            }
            finally
            {
                connection.Sync.Release();
            }
        }

        foreach (var id in disconnected)
        {
            RemoveEventClient(id);
        }
    }

    private void RemoveEventClient(int id)
    {
        if (_eventClients.TryRemove(id, out var removed))
        {
            removed.Dispose();
        }
    }

    private static IpcMessage CreateErrorResponse(string? id, string name, string error)
    {
        return new IpcMessage
        {
            Type = IpcMessageKinds.Response,
            Id = id,
            Name = name,
            Payload = JsonSerializer.SerializeToElement(new { ok = false, message = error }, JsonOptions),
            Error = error
        };
    }

    private sealed class EventClientConnection(NamedPipeServerStream stream) : IDisposable
    {
        public NamedPipeServerStream Stream { get; } = stream;
        public StreamWriter Writer { get; } = new(stream) { AutoFlush = true };
        public SemaphoreSlim Sync { get; } = new(1, 1);

        public void Dispose()
        {
            Sync.Dispose();
            Writer.Dispose();
            Stream.Dispose();
        }
    }
}
