namespace VolumePadLink.Contracts.DTOs;

public sealed record DisplayModelDto(
    string Screen,
    string Title,
    string Subtitle,
    string ValueText,
    string? IconRef,
    bool Muted,
    string Accent);

public sealed record LedMeterModelDto(
    string Mode,
    float Rms,
    float Peak,
    bool Muted,
    string Theme,
    float Brightness);
