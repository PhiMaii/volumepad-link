using System.IO.Ports;
using System.Text;
using System.Text.Json;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Transport.Device;

public sealed class SerialDeviceLink(ILogger<SerialDeviceLink> logger) : IDeviceLink
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private SerialPort? _serialPort;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public event EventHandler<ProtocolEnvelope>? MessageReceived;

    public bool IsConnected => _serialPort?.IsOpen == true;
    public string? PortName => _serialPort?.PortName;

    public async Task ConnectAsync(string portName, CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            await DisconnectAsync(cancellationToken);
        }

        var serialPort = new SerialPort(portName, 115200)
        {
            NewLine = "\n",
            Encoding = Encoding.UTF8,
            DtrEnable = true,
            RtsEnable = true,
        };

        serialPort.Open();

        _serialPort = serialPort;
        _reader = new StreamReader(serialPort.BaseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(serialPort.BaseStream, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        _readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);

        logger.LogInformation("Serial device connected on {Port}", portName);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var cts = _readCts;
        var task = _readTask;
        _readCts = null;
        _readTask = null;

        if (cts is not null)
        {
            await cts.CancelAsync();
        }

        if (task is not null)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (Exception)
            {
                // ignored on shutdown
            }
        }

        _reader?.Dispose();
        _writer?.Dispose();
        if (_serialPort is not null)
        {
            try
            {
                _serialPort.Close();
            }
            catch (Exception)
            {
                // ignored
            }
            _serialPort.Dispose();
        }

        _reader = null;
        _writer = null;
        _serialPort = null;
        cts?.Dispose();
    }

    public async Task SendAsync(ProtocolEnvelope message, CancellationToken cancellationToken)
    {
        var writer = _writer ?? throw new InvalidOperationException("Serial link is not connected.");
        var json = JsonSerializer.Serialize(message, ProtocolJson.SerializerOptions);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteLineAsync(json);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
        _writeGate.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _reader;
        if (reader is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
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
                logger.LogWarning(ex, "Serial read failed.");
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(line, ProtocolJson.SerializerOptions);
                if (envelope is null)
                {
                    continue;
                }

                MessageReceived?.Invoke(this, envelope);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Ignoring malformed serial JSON line: {Line}", line);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed handling serial message.");
            }
        }
    }
}
