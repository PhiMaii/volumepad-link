using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VolumePadLink.Agent.Services.Device;
using VolumePadLink.Agent.Services.Interfaces;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class SimulatedDeviceSessionTests
{
    [Fact]
    public async Task Simulator_EmitsCapabilities_AndAcksSettingsApply()
    {
        var codec = new DeviceProtocolCodec();
        var messages = new ConcurrentQueue<DeviceProtocolEnvelope>();

        var loggerFactory = LoggerFactory.Create(builder => { });
        await using var simulator = new SimulatedDeviceSession(
            codec,
            (line, cancellationToken) =>
            {
                if (codec.TryParseEnvelope(line, out var envelope, out var parseError) && envelope is not null)
                {
                    messages.Enqueue(envelope);
                }

                return Task.CompletedTask;
            },
            loggerFactory.CreateLogger<SimulatedDeviceSession>());

        await simulator.StartAsync(CancellationToken.None);
        var settingsLine = codec.Encode("settings.apply", new
        {
            detentCount = 24,
            detentStrength = 0.7,
            snapStrength = 0.5,
            ledBrightness = 0.9,
            displayBrightness = 0.8,
            encoderInvert = true,
            buttonLongPressMs = 350
        }, "req1");

        await simulator.ProcessOutboundAsync(settingsLine, CancellationToken.None);

        var capabilities = await WaitForAsync(messages, m => m.Type == "capabilities", TimeSpan.FromSeconds(2));
        var settingsAck = await WaitForAsync(messages, m => m.Type == "ack" && m.RequestId == "req1", TimeSpan.FromSeconds(2));

        Assert.NotNull(capabilities);
        Assert.NotNull(settingsAck);
    }

    private static async Task<DeviceProtocolEnvelope?> WaitForAsync(
        ConcurrentQueue<DeviceProtocolEnvelope> queue,
        Func<DeviceProtocolEnvelope, bool> predicate,
        TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - started < timeout)
        {
            if (queue.TryDequeue(out var envelope))
            {
                if (predicate(envelope))
                {
                    return envelope;
                }
            }
            else
            {
                await Task.Delay(20);
            }
        }

        return null;
    }
}

