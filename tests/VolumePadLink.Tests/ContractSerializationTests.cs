using System.Text.Json;
using VolumePadLink.Contracts.Abstractions;
using VolumePadLink.Contracts.Commands;
using Xunit;

namespace VolumePadLink.Tests;

public sealed class ContractSerializationTests
{
    [Fact]
    public void IpcMessage_RoundTrips_WithTypedPayload()
    {
        var original = new IpcMessage
        {
            Type = IpcMessageKinds.Command,
            Id = "123",
            Name = CommandNames.AudioSetMasterVolume,
            Payload = JsonSerializer.SerializeToElement(new SetMasterVolumeRequest(0.72f))
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<IpcMessage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Type, deserialized!.Type);
        Assert.Equal(original.Name, deserialized.Name);

        var payload = JsonSerializer.Deserialize<SetMasterVolumeRequest>(deserialized.Payload.GetRawText());
        Assert.NotNull(payload);
        Assert.Equal(0.72f, payload!.Value);
    }
}
