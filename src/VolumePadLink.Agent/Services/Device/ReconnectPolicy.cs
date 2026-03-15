using VolumePadLink.Agent.Services.Interfaces;

namespace VolumePadLink.Agent.Services.Device;

public sealed class ReconnectPolicy : IReconnectPolicy
{
    private static readonly int[] BackoffMs = [1500, 3000, 5000, 8000, 10000];
    private readonly Random _jitter = new();
    private readonly object _sync = new();

    public TimeSpan GetDelay(int attempt)
    {
        var normalizedAttempt = Math.Max(attempt, 1);
        var index = Math.Min(normalizedAttempt - 1, BackoffMs.Length - 1);
        var baseDelayMs = BackoffMs[index];

        int jitterMs;
        lock (_sync)
        {
            jitterMs = _jitter.Next(-200, 201);
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, baseDelayMs + jitterMs));
    }
}
