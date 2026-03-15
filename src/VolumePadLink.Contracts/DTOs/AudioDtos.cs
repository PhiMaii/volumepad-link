namespace VolumePadLink.Contracts.DTOs;

public sealed record AudioGraphDto(
    MasterAudioDto Master,
    IReadOnlyList<AudioSessionDto> Sessions,
    DateTimeOffset CapturedAtUtc);

public sealed record AudioSessionDto(
    string SessionId,
    int ProcessId,
    string ProcessName,
    string DisplayName,
    float Volume,
    bool Muted,
    float Peak,
    float Rms,
    string? IconKey,
    bool IsActive);

public sealed record MasterAudioDto(
    string EndpointId,
    string EndpointName,
    float Volume,
    bool Muted,
    float Peak,
    float Rms);
