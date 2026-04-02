namespace VolumePadLink.Contracts.Models;

public sealed record ServiceRestartAudioBackendResponse(bool Restarted);

public sealed record DiagnosticsEvent(
    string Level,
    string Code,
    string Message,
    DateTimeOffset UtcNow,
    IReadOnlyDictionary<string, string>? Metadata = null);
