using System.Text.RegularExpressions;

namespace VolumePadLink.Contracts.Models;

public static class MeterModes
{
    public const string RingFill = "ring_fill";
    public const string VuPeakHold = "vu_peak_hold";
    public const string PeakIndicator = "peak_indicator";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        RingFill,
        VuPeakHold,
        PeakIndicator,
    };

    public static bool IsValid(string value) => Allowed.Contains(value);
}

public sealed class AppSettings
{
    public bool AutoReconnectOnError { get; set; } = true;
    public bool AutoConnectOnStartup { get; set; }

    public double VolumeStepSize { get; set; } = 0.02;
    public int DetentCount { get; set; } = 24;
    public double DetentStrength { get; set; } = 0.65;
    public double SnapStrength { get; set; } = 0.40;
    public bool EncoderInvert { get; set; }
    public double LedBrightness { get; set; } = 0.80;

    public string MeterMode { get; set; } = MeterModes.RingFill;
    public string MeterColor { get; set; } = "#00D26A";
    public double MeterBrightness { get; set; } = 0.80;
    public double MeterGain { get; set; } = 1.0;
    public double MeterSmoothing { get; set; } = 0.25;
    public int MeterPeakHoldMs { get; set; } = 500;
    public int MeterMuteRedDurationMs { get; set; } = 700;

    public bool LowEndstopEnabled { get; set; } = true;
    public double LowEndstopPosition { get; set; } = -1.0;
    public double LowEndstopStrength { get; set; } = 0.70;

    public bool HighEndstopEnabled { get; set; } = true;
    public double HighEndstopPosition { get; set; } = 1.0;
    public double HighEndstopStrength { get; set; } = 0.70;

    public AppSettings Clone()
    {
        return new AppSettings
        {
            AutoReconnectOnError = AutoReconnectOnError,
            AutoConnectOnStartup = AutoConnectOnStartup,
            VolumeStepSize = VolumeStepSize,
            DetentCount = DetentCount,
            DetentStrength = DetentStrength,
            SnapStrength = SnapStrength,
            EncoderInvert = EncoderInvert,
            LedBrightness = LedBrightness,
            MeterMode = MeterMode,
            MeterColor = MeterColor,
            MeterBrightness = MeterBrightness,
            MeterGain = MeterGain,
            MeterSmoothing = MeterSmoothing,
            MeterPeakHoldMs = MeterPeakHoldMs,
            MeterMuteRedDurationMs = MeterMuteRedDurationMs,
            LowEndstopEnabled = LowEndstopEnabled,
            LowEndstopPosition = LowEndstopPosition,
            LowEndstopStrength = LowEndstopStrength,
            HighEndstopEnabled = HighEndstopEnabled,
            HighEndstopPosition = HighEndstopPosition,
            HighEndstopStrength = HighEndstopStrength,
        };
    }
}

public sealed record SettingsGetResponse(AppSettings Effective);

public sealed record SettingsUpdateRequest(AppSettings Settings);

public sealed record SettingsUpdateResponse(AppSettings Effective);

public sealed record SettingsAppliedEvent(AppSettings Effective);

public sealed record SettingsValidationResult(bool IsValid, AppSettings Effective, IReadOnlyList<string> Errors);

public static class SettingsValidator
{
    private static readonly Regex HexColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public static SettingsValidationResult ValidateAndNormalize(AppSettings? input)
    {
        var working = (input ?? new AppSettings()).Clone();
        var errors = new List<string>();

        working.VolumeStepSize = Clamp(working.VolumeStepSize, 0.001, 0.20, nameof(working.VolumeStepSize), errors);
        working.DetentCount = Clamp(working.DetentCount, 0, 128, nameof(working.DetentCount), errors);
        working.DetentStrength = Clamp(working.DetentStrength, 0.0, 1.0, nameof(working.DetentStrength), errors);
        working.SnapStrength = Clamp(working.SnapStrength, 0.0, 1.0, nameof(working.SnapStrength), errors);
        working.LedBrightness = Clamp(working.LedBrightness, 0.0, 1.0, nameof(working.LedBrightness), errors);
        working.MeterBrightness = Clamp(working.MeterBrightness, 0.0, 1.0, nameof(working.MeterBrightness), errors);
        working.MeterGain = Clamp(working.MeterGain, 0.10, 8.0, nameof(working.MeterGain), errors);
        working.MeterSmoothing = Clamp(working.MeterSmoothing, 0.0, 1.0, nameof(working.MeterSmoothing), errors);
        working.MeterPeakHoldMs = Clamp(working.MeterPeakHoldMs, 0, 3000, nameof(working.MeterPeakHoldMs), errors);
        working.MeterMuteRedDurationMs = Clamp(working.MeterMuteRedDurationMs, 50, 3000, nameof(working.MeterMuteRedDurationMs), errors);
        working.LowEndstopPosition = Clamp(working.LowEndstopPosition, -1.0, 1.0, nameof(working.LowEndstopPosition), errors);
        working.HighEndstopPosition = Clamp(working.HighEndstopPosition, -1.0, 1.0, nameof(working.HighEndstopPosition), errors);
        working.LowEndstopStrength = Clamp(working.LowEndstopStrength, 0.0, 1.0, nameof(working.LowEndstopStrength), errors);
        working.HighEndstopStrength = Clamp(working.HighEndstopStrength, 0.0, 1.0, nameof(working.HighEndstopStrength), errors);

        if (!MeterModes.IsValid(working.MeterMode))
        {
            errors.Add($"MeterMode must be one of: {MeterModes.RingFill}, {MeterModes.VuPeakHold}, {MeterModes.PeakIndicator}.");
            working.MeterMode = MeterModes.RingFill;
        }

        if (!HexColorRegex.IsMatch(working.MeterColor))
        {
            errors.Add("MeterColor must be a hex color in #RRGGBB format.");
            working.MeterColor = "#00D26A";
        }

        if (working.LowEndstopPosition >= working.HighEndstopPosition)
        {
            errors.Add("LowEndstopPosition must be lower than HighEndstopPosition.");
        }

        return new SettingsValidationResult(errors.Count == 0, working, errors);
    }

    private static double Clamp(double value, double min, double max, string field, ICollection<string> errors)
    {
        if (value < min || value > max)
        {
            errors.Add($"{field} must be in range [{min}, {max}].");
        }

        return Math.Clamp(value, min, max);
    }

    private static int Clamp(int value, int min, int max, string field, ICollection<string> errors)
    {
        if (value < min || value > max)
        {
            errors.Add($"{field} must be in range [{min}, {max}].");
        }

        return Math.Clamp(value, min, max);
    }
}
