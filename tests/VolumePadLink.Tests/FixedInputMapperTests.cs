using VolumePadLink.Agent.Services;
using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Tests;

public sealed class FixedInputMapperTests
{
    [Fact]
    public void MapButton_Button1Press_MapsToToggleMute()
    {
        var action = FixedInputMapper.MapButton(new DeviceButtonInput
        {
            ButtonId = 1,
            Action = "press",
        });

        Assert.Equal(FixedInputActionType.ToggleMute, action.Type);
    }

    [Fact]
    public void MapButton_NonConfiguredButton_MapsToNone()
    {
        var action = FixedInputMapper.MapButton(new DeviceButtonInput
        {
            ButtonId = 2,
            Action = "press",
        });

        Assert.Equal(FixedInputActionType.None, action.Type);
    }

    [Theory]
    [InlineData(false, 2, 0.04)]
    [InlineData(true, 2, -0.04)]
    [InlineData(false, -2, -0.04)]
    public void MapEncoder_MapsDeltaUsingStepAndInvert(bool invert, int deltaSteps, double expectedDelta)
    {
        var settings = new AppSettings
        {
            EncoderInvert = invert,
            VolumeStepSize = 0.02,
        };

        var action = FixedInputMapper.MapEncoder(new DeviceEncoderInput
        {
            DeltaSteps = deltaSteps,
            Pressed = false,
        }, settings);

        Assert.Equal(FixedInputActionType.VolumeDelta, action.Type);
        Assert.Equal(expectedDelta, action.Value, 6);
    }
}
