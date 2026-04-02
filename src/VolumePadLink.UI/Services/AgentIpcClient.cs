using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.UI.Services;

public sealed class AgentIpcClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ProtocolEnvelope>> _pending = new(StringComparer.Ordinal);

    private NamedPipeClientStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _readTask;
    private CancellationTokenSource? _readCts;

    public bool IsConnected => _stream?.IsConnected == true;

    public event EventHandler<ProtocolEnvelope>? EventReceived;
    public event EventHandler<string>? ConnectionLost;

    public async Task ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        _stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _stream.ConnectAsync(cancellationToken);

        _reader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);
    }

    public async Task<TResponse> SendRequestAsync<TResponse>(string methodName, object payload, CancellationToken cancellationToken)
    {
        if (!IsConnected || _writer is null)
        {
            throw new InvalidOperationException("Agent IPC connection is not established.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var request = new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Request,
            V = ProtocolConstants.Version,
            Id = requestId,
            Name = methodName,
            TsUtc = DateTimeOffset.UtcNow,
            Payload = ProtocolJson.ToElement(payload),
        };

        var completion = new TaskCompletionSource<ProtocolEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Failed to queue request.");
        }

        var json = JsonSerializer.Serialize(request, ProtocolJson.SerializerOptions);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(json);
        }
        finally
        {
            _writeGate.Release();
        }

        var response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        if (!response.Ok.GetValueOrDefault())
        {
            var code = response.Error?.Code ?? ProtocolNames.ErrorCodes.InternalError;
            var message = response.Error?.Message ?? "Unknown agent error";
            throw new InvalidOperationException($"{code}: {message}");
        }

        if (typeof(TResponse) == typeof(object))
        {
            return (TResponse)(object)new object();
        }

        return ProtocolJson.DeserializePayload<TResponse>(response);
    }

    public async ValueTask DisposeAsync()
    {
        if (_readCts is not null)
        {
            await _readCts.CancelAsync();
            _readCts.Dispose();
            _readCts = null;
        }

        if (_readTask is not null)
        {
            try
            {
                await _readTask;
            }
            catch (Exception)
            {
                // ignored
            }
        }

        foreach (var pending in _pending.ToArray())
        {
            if (_pending.TryRemove(pending.Key, out var tcs))
            {
                tcs.TrySetException(new IOException("IPC disconnected."));
            }
        }

        _reader?.Dispose();
        _writer?.Dispose();
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }
        _writeGate.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_reader is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(line, ProtocolJson.SerializerOptions);
                if (envelope is null)
                {
                    continue;
                }

                if (envelope.Type == ProtocolMessageType.Response && !string.IsNullOrWhiteSpace(envelope.Id))
                {
                    if (_pending.TryRemove(envelope.Id, out var completion))
                    {
                        completion.TrySetResult(envelope);
                    }
                    continue;
                }

                if (envelope.Type == ProtocolMessageType.Event)
                {
                    EventReceived?.Invoke(this, envelope);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            ConnectionLost?.Invoke(this, ex.Message);
        }
        finally
        {
            ConnectionLost?.Invoke(this, "Agent IPC connection closed.");
        }
    }
}
