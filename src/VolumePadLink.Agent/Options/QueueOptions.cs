namespace VolumePadLink.Agent.Options;

public sealed class QueueOptions
{
    public const string SectionName = "Queues";

    public int EventCapacity { get; set; } = 512;
    public int DeviceHighCapacity { get; set; } = 128;
    public int DeviceMediumCapacity { get; set; } = 128;
    public int DeviceLowCapacity { get; set; } = 64;
    public int RingControlCapacity { get; set; } = 128;
    public int RingAnimationCapacity { get; set; } = 96;
    public int RingMeterCapacity { get; set; } = 1;
}
