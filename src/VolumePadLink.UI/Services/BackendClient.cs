using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VolumePadLink.Contracts.Abstractions;

namespace VolumePadLink.UI.Services;

public sealed class BackendClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public event Func<IpcMessage, Task>? EventReceived;

    public async Task<TResponse> SendCommandAsync<TRequest, TResponse>(string commandName, TRequest request, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", IpcDefaults.CommandPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1500, cancellationToken);

        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };

        var message = new IpcMessage
        {
            Type = IpcMessageKinds.Command,
            Id = Guid.NewGuid().ToString("N"),
            Name = commandName,
            Payload = JsonSerializer.SerializeToElement(request, JsonOptions)
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions));

        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            throw new InvalidOperationException("Agent closed the command connection before replying.");
        }

        var response = JsonSerializer.Deserialize<IpcMessage>(line, JsonOptions);
        if (response is null)
        {
            throw new InvalidOperationException("Agent returned an invalid response envelope.");
        }

        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            throw new InvalidOperationException(response.Error);
        }

        var typed = JsonSerializer.Deserialize<TResponse>(response.Payload.GetRawText(), JsonOptions);
        if (typed is null)
        {
            throw new InvalidOperationException($"Unable to parse response payload as {typeof(TResponse).Name}.");
        }

        return typed;
    }

    public async Task RunEventLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", IpcDefaults.EventPipeName, PipeDirection.In, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000, cancellationToken);

                using var reader = new StreamReader(pipe);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    var message = JsonSerializer.Deserialize<IpcMessage>(line, JsonOptions);
                    if (message is null)
                    {
                        continue;
                    }

                    if (EventReceived is { } handlers)
                    {
                        foreach (Func<IpcMessage, Task> handler in handlers.GetInvocationList())
                        {
                            await handler(message);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(500, cancellationToken);
            }
        }
    }
}
