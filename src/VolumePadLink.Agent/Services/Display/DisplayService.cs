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
    private int _lastModelHash;
    private bool _hasLastModelHash;

    public async Task PushModelAsync(DisplayModelDto model, CancellationToken cancellationToken = default)
    {
        var modelHash = ComputeModelHash(model);

        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_hasLastModelHash && _lastModelHash == modelHash)
            {
                return;
            }

            // Keep semantic transport for phase-1 compatibility.
            await deviceService.SendDisplayModelAsync(model, cancellationToken);

            var currentFrame = framebufferRenderer.Render(model);
            var diff = dirtyRegionTracker.Diff(_previousFrame, currentFrame);
            _previousFrame = currentFrame;

            if (diff is not null)
            {
                if (diff.IsFullFrame)
                {
                    await deviceService.SendRawMessageAsync("display.fullFrame", new
                    {
                        width = currentFrame.Width,
                        height = currentFrame.Height,
                        encoding = "gray8",
                        data = Convert.ToBase64String(currentFrame.Pixels)
                    }, cancellationToken);
                }
                else if (diff.DirtyRect is not null)
                {
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
            }

            _lastModelHash = modelHash;
            _hasLastModelHash = true;
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

    private static int ComputeModelHash(DisplayModelDto model)
    {
        return HashCode.Combine(
            model.Screen,
            model.Title,
            model.Subtitle,
            model.ValueText,
            model.IconRef,
            model.Muted,
            model.Accent);
    }
}
