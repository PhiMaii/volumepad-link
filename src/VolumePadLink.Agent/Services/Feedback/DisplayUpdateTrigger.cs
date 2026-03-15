using System.Text.Json;
using System.Threading.Channels;
using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Feedback;

public sealed class DisplayUpdateTrigger(
    IEventHub eventHub,
    IDisplayService displayService,
    ITargetService targetService,
    ILogger<DisplayUpdateTrigger> logger) : BackgroundService, IDisplayUpdateTrigger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Channel<bool> _trigger = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        eventHub.EventPublished += OnEventPublishedAsync;

        try
        {
            while (await _trigger.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_trigger.Reader.TryRead(out _))
                {
                    // Coalesce spikes into one render.
                }

                try
                {
                    await displayService.PushTargetStatusAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Display trigger update skipped due to transient error.");
                }
            }
        }
        finally
        {
            eventHub.EventPublished -= OnEventPublishedAsync;
        }
    }

    private async Task OnEventPublishedAsync(IpcMessage message)
    {
        switch (message.Name)
        {
            case EventNames.TargetActiveChanged:
            case EventNames.DeviceConnected:
            case EventNames.AudioMasterChanged:
                _trigger.Writer.TryWrite(true);
                return;

            case EventNames.AudioSessionUpdated:
                if (!TryDeserializePayload<AudioSessionChangedEvent>(message, out var sessionEvent) || sessionEvent is null)
                {
                    return;
                }

                var activeTarget = await targetService.GetActiveTargetAsync();
                if (activeTarget.Kind == TargetKinds.SessionById &&
                    string.Equals(activeTarget.SessionId, sessionEvent.Session.SessionId, StringComparison.OrdinalIgnoreCase))
                {
                    _trigger.Writer.TryWrite(true);
                }

                return;

            default:
                return;
        }
    }

    private static bool TryDeserializePayload<T>(IpcMessage message, out T? payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<T>(message.Payload.GetRawText(), JsonOptions);
            return payload is not null;
        }
        catch
        {
            payload = default;
            return false;
        }
    }
}
