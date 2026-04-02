using System.Threading.Channels;
using Microsoft.Extensions.Options;
using VolumePadLink.Agent.Options;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Agent.Services.Ring;

public sealed class RingRenderService(
    IDeviceService deviceService,
    IOptions<QueueOptions> queueOptions,
    ILogger<RingRenderService> logger) : BackgroundService, IRingRenderService
{
    private readonly Channel<RingControlCommand> _controlQueue = Channel.CreateBounded<RingControlCommand>(new BoundedChannelOptions(Math.Max(32, queueOptions.Value.RingControlCapacity))
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly Channel<DeviceRingStreamFrame> _animationQueue = Channel.CreateBounded<DeviceRingStreamFrame>(new BoundedChannelOptions(Math.Max(8, queueOptions.Value.RingAnimationCapacity))
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly Channel<MeterTick> _meterQueue = Channel.CreateBounded<MeterTick>(new BoundedChannelOptions(Math.Max(1, queueOptions.Value.RingMeterCapacity))
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly RingOwnerArbitrator _arbitrator = new();

    public ValueTask QueueMeterFrameAsync(MeterTick meterTick, CancellationToken cancellationToken)
    {
        return _meterQueue.Writer.WriteAsync(meterTick, cancellationToken);
    }

    public ValueTask BeginAnimationAsync(string streamId, string reason, CancellationToken cancellationToken)
    {
        return _controlQueue.Writer.WriteAsync(new BeginAnimationCommand(streamId, reason), cancellationToken);
    }

    public ValueTask QueueAnimationFrameAsync(DeviceRingStreamFrame frame, CancellationToken cancellationToken)
    {
        return _animationQueue.Writer.WriteAsync(frame, cancellationToken);
    }

    public ValueTask EndAnimationAsync(string streamId, CancellationToken cancellationToken)
    {
        return _controlQueue.Writer.WriteAsync(new EndAnimationCommand(streamId), cancellationToken);
    }

    public ValueTask SetLedAsync(DeviceRingSetLed led, CancellationToken cancellationToken)
    {
        return _controlQueue.Writer.WriteAsync(new SetLedCommand(led), cancellationToken);
    }

    public ValueTask TriggerMuteOverrideAsync(int durationMs, CancellationToken cancellationToken)
    {
        return _controlQueue.Writer.WriteAsync(new MuteOverrideCommand(Math.Max(50, durationMs)), cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (_controlQueue.Reader.TryRead(out var controlCommand))
            {
                await HandleControlCommandAsync(controlCommand, stoppingToken);
            }

            _arbitrator.Tick(DateTimeOffset.UtcNow);
            var owner = _arbitrator.Owner;

            if (owner == RingRenderOwner.Animation && _animationQueue.Reader.TryRead(out var animationFrame))
            {
                if (string.Equals(animationFrame.StreamId, _arbitrator.ActiveStreamId, StringComparison.OrdinalIgnoreCase))
                {
                    await SendAnimationFrameAsync(animationFrame, stoppingToken);
                }
                continue;
            }

            if (owner == RingRenderOwner.Meter && _meterQueue.Reader.TryRead(out var meterTick))
            {
                await SendMeterFrameAsync(meterTick, stoppingToken);
                continue;
            }

            await WaitForWorkAsync(stoppingToken);
        }
    }

    private async Task HandleControlCommandAsync(RingControlCommand command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case BeginAnimationCommand begin:
                _arbitrator.BeginAnimation(begin.StreamId);

                await deviceService.QueueCommandAsync(
                    ProtocolNames.DeviceMethods.DeviceRingStreamBegin,
                    new DeviceRingStreamBegin
                    {
                        StreamId = begin.StreamId,
                        Reason = begin.Reason,
                        LedCount = 27,
                    },
                    DeviceCommandPriority.Low,
                    cancellationToken);
                break;

            case EndAnimationCommand end:
                await deviceService.QueueCommandAsync(
                    ProtocolNames.DeviceMethods.DeviceRingStreamEnd,
                    new DeviceRingStreamEnd
                    {
                        StreamId = end.StreamId,
                    },
                    DeviceCommandPriority.Low,
                    cancellationToken);
                _arbitrator.EndAnimation(end.StreamId);
                break;

            case SetLedCommand setLed:
                await deviceService.QueueCommandAsync(
                    ProtocolNames.DeviceMethods.DeviceRingSetLed,
                    setLed.Led,
                    DeviceCommandPriority.Low,
                    cancellationToken);
                break;

            case MuteOverrideCommand muteOverride:
                _arbitrator.TriggerMuteOverride(muteOverride.DurationMs, DateTimeOffset.UtcNow);
                logger.LogDebug("Ring owner set to mute override for {DurationMs}ms", muteOverride.DurationMs);

                await deviceService.QueueCommandAsync(
                    ProtocolNames.DeviceMethods.DeviceRingMuteOverride,
                    new DeviceRingMuteOverride
                    {
                        Color = "#FF0000",
                        DurationMs = muteOverride.DurationMs,
                    },
                    DeviceCommandPriority.High,
                    cancellationToken);
                break;
        }
    }

    private async Task SendMeterFrameAsync(MeterTick meterTick, CancellationToken cancellationToken)
    {
        await deviceService.QueueCommandAsync(
            ProtocolNames.DeviceMethods.DeviceMeterFrame,
            meterTick,
            DeviceCommandPriority.Low,
            cancellationToken);
    }

    private async Task SendAnimationFrameAsync(DeviceRingStreamFrame frame, CancellationToken cancellationToken)
    {
        await deviceService.QueueCommandAsync(
            ProtocolNames.DeviceMethods.DeviceRingStreamFrame,
            frame,
            DeviceCommandPriority.Low,
            cancellationToken);
    }

    private async Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        var waitTasks = new List<Task>
        {
            _controlQueue.Reader.WaitToReadAsync(cancellationToken).AsTask(),
            _animationQueue.Reader.WaitToReadAsync(cancellationToken).AsTask(),
            _meterQueue.Reader.WaitToReadAsync(cancellationToken).AsTask(),
        };

        var muteDelay = _arbitrator.GetMuteDelay(DateTimeOffset.UtcNow);

        if (muteDelay is not null && muteDelay.Value > TimeSpan.Zero)
        {
            waitTasks.Add(Task.Delay(muteDelay.Value, cancellationToken));
        }

        await Task.WhenAny(waitTasks);
    }

    private abstract record RingControlCommand;
    private sealed record BeginAnimationCommand(string StreamId, string Reason) : RingControlCommand;
    private sealed record EndAnimationCommand(string StreamId) : RingControlCommand;
    private sealed record SetLedCommand(DeviceRingSetLed Led) : RingControlCommand;
    private sealed record MuteOverrideCommand(int DurationMs) : RingControlCommand;
}

public enum RingRenderOwner
{
    Meter,
    Animation,
    MuteOverride,
}
