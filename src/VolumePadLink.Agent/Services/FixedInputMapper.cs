using VolumePadLink.Contracts.Models;

namespace VolumePadLink.Agent.Services;

public enum FixedInputActionType
{
    None,
    ToggleMute,
    VolumeDelta,
}

public readonly record struct FixedInputAction(FixedInputActionType Type, double Value = 0.0)
{
    public static FixedInputAction None { get; } = new(FixedInputActionType.None);
}

public static class FixedInputMapper
{
    public static FixedInputAction MapButton(DeviceButtonInput input)
    {
        if (!string.Equals(input.Action, "press", StringComparison.OrdinalIgnoreCase))
        {
            return FixedInputAction.None;
        }

        return input.ButtonId == 1
            ? new FixedInputAction(FixedInputActionType.ToggleMute)
            : FixedInputAction.None;
    }

    public static FixedInputAction MapEncoder(DeviceEncoderInput input, AppSettings settings)
    {
        if (input.Pressed || input.DeltaSteps == 0)
        {
            return FixedInputAction.None;
        }

        var direction = settings.EncoderInvert ? -1 : 1;
        var delta = direction * settings.VolumeStepSize * input.DeltaSteps;
        return new FixedInputAction(FixedInputActionType.VolumeDelta, delta);
    }
}
