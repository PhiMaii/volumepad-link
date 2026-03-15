using VolumePadLink.Agent.Services.Interfaces;
using VolumePadLink.Agent.State;
using VolumePadLink.Contracts.DTOs;

namespace VolumePadLink.Agent.Services.Display;

public sealed class DisplayService(
    AgentStateStore stateStore,
    IDeviceService deviceService,
    IAudioService audioService,
    ITargetService targetService,
    IFramebufferRenderer framebufferRenderer,
    IDirtyRegionTracker dirtyRegionTracker) : IDisplayService
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private DisplayFrame? _previousFrame;

    public async Task PushModelAsync(DisplayModelDto model, CancellationToken cancellationToken = default)
    {
        // Keep semantic transport for phase-1 compatibility.
        await deviceService.SendDisplayModelAsync(model, cancellationToken);

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var currentFrame = framebufferRenderer.Render(model);
            var diff = dirtyRegionTracker.Diff(_previousFrame, currentFrame);
            _previousFrame = currentFrame;

            if (diff is null)
            {
                return;
            }

            if (diff.IsFullFrame)
            {
                await deviceService.SendRawMessageAsync("display.fullFrame", new
                {
                    width = currentFrame.Width,
                    height = currentFrame.Height,
                    encoding = "gray8",
                    data = Convert.ToBase64String(currentFrame.Pixels)
                }, cancellationToken);

                return;
            }

            if (diff.DirtyRect is null)
            {
                return;
            }

            await deviceService.SendRawMessageAsync("display.beginFrame", new { width = currentFrame.Width, height = currentFrame.Height }, cancellationToken);
            await deviceService.SendRawMessageAsync("display.rect", new
            {
                x = diff.DirtyRect.X,
                y = diff.DirtyRect.Y,
                width = diff.DirtyRect.Width,
                height = diff.DirtyRect.Height,
                encoding = "gray8",
                data = Convert.ToBase64String(diff.DirtyRect.Pixels)
            }, cancellationToken);
            await deviceService.SendRawMessageAsync("display.endFrame", new { }, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task PushTargetStatusAsync(CancellationToken cancellationToken = default)
    {
        var graph = await audioService.GetGraphAsync(cancellationToken);
        var target = await targetService.GetActiveTargetAsync(cancellationToken);
        var settings = stateStore.GetSettings();

        string title;
        float volume;
        bool muted;

        if (target.Kind == TargetKinds.SessionById && !string.IsNullOrWhiteSpace(target.SessionId))
        {
            var session = graph.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, target.SessionId, StringComparison.OrdinalIgnoreCase));
            if (session is null)
            {
                title = "Master";
                volume = graph.Master.Volume;
                muted = graph.Master.Muted;
            }
            else
            {
                title = session.DisplayName;
                volume = session.Volume;
                muted = session.Muted;
            }
        }
        else
        {
            title = "Master";
            volume = graph.Master.Volume;
            muted = graph.Master.Muted;
        }

        var model = new DisplayModelDto(
            "target-status",
            title,
            muted ? "Muted" : "Volume",
            $"{Math.Round(volume * 100)}%",
            null,
            muted,
            settings.EncoderInvert ? "#FFAE42" : "#00D26A");

        await PushModelAsync(model, cancellationToken);
    }
}
