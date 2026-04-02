using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Tests;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void ValidateAndNormalize_ValidSettings_PassesWithoutErrors()
    {
        var input = new AppSettings
        {
            MeterMode = MeterModes.VuPeakHold,
            MeterColor = "#ABCDEF",
            MeterGain = 2.5,
            VolumeStepSize = 0.05,
            LowEndstopPosition = -0.8,
            HighEndstopPosition = 0.9,
        };

        var result = SettingsValidator.ValidateAndNormalize(input);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(MeterModes.VuPeakHold, result.Effective.MeterMode);
        Assert.Equal("#ABCDEF", result.Effective.MeterColor);
        Assert.Equal(2.5, result.Effective.MeterGain, 6);
    }

    [Fact]
    public void ValidateAndNormalize_OutOfRangeValues_AreClampedAndReported()
    {
        var input = new AppSettings
        {
            VolumeStepSize = 10.0,
            MeterBrightness = -2.0,
            MeterGain = 99.0,
            MeterMuteRedDurationMs = 5,
            MeterColor = "green",
            MeterMode = "invalid_mode",
            LowEndstopPosition = 0.5,
            HighEndstopPosition = 0.5,
        };

        var result = SettingsValidator.ValidateAndNormalize(input);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0.20, result.Effective.VolumeStepSize, 4);
        Assert.Equal(0.0, result.Effective.MeterBrightness, 4);
        Assert.Equal(8.0, result.Effective.MeterGain, 4);
        Assert.Equal(50, result.Effective.MeterMuteRedDurationMs);
        Assert.Equal(MeterModes.RingFill, result.Effective.MeterMode);
        Assert.Equal("#00D26A", result.Effective.MeterColor);
        Assert.Contains(result.Errors, message => message.Contains("LowEndstopPosition must be lower"));
    }
}
