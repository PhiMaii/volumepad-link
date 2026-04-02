namespace VolumePadLink.Agent.Services.Ring;

public sealed class RingOwnerArbitrator
{
    private string? _activeStreamId;
    private DateTimeOffset? _muteOverrideUntilUtc;

    public RingRenderOwner Owner { get; private set; } = RingRenderOwner.Meter;
    public string? ActiveStreamId => _activeStreamId;

    public void BeginAnimation(string streamId)
    {
        _activeStreamId = streamId;
        if (Owner != RingRenderOwner.MuteOverride)
        {
            Owner = RingRenderOwner.Animation;
        }
    }

    public void EndAnimation(string streamId)
    {
        if (!string.Equals(_activeStreamId, streamId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeStreamId = null;
        if (Owner != RingRenderOwner.MuteOverride)
        {
            Owner = RingRenderOwner.Meter;
        }
    }

    public void TriggerMuteOverride(int durationMs, DateTimeOffset nowUtc)
    {
        _muteOverrideUntilUtc = nowUtc.AddMilliseconds(durationMs);
        Owner = RingRenderOwner.MuteOverride;
    }

    public void Tick(DateTimeOffset nowUtc)
    {
        if (Owner != RingRenderOwner.MuteOverride || _muteOverrideUntilUtc is null)
        {
            return;
        }

        if (nowUtc < _muteOverrideUntilUtc.Value)
        {
            return;
        }

        _muteOverrideUntilUtc = null;
        Owner = string.IsNullOrWhiteSpace(_activeStreamId)
            ? RingRenderOwner.Meter
            : RingRenderOwner.Animation;
    }

    public TimeSpan? GetMuteDelay(DateTimeOffset nowUtc)
    {
        if (Owner != RingRenderOwner.MuteOverride || _muteOverrideUntilUtc is null)
        {
            return null;
        }

        return _muteOverrideUntilUtc.Value - nowUtc;
    }
}
