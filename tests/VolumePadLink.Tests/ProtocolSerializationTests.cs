using System.Text.Json;
using VolumePadLink.Contracts.Models;
using VolumePadLink.Contracts.Protocol;

namespace VolumePadLink.Tests;

public sealed class ProtocolSerializationTests
{
    [Fact]
    public void ProtocolEnvelope_RoundTrips_WithExpectedWireShape()
    {
        var envelope = new ProtocolEnvelope
        {
            Type = ProtocolMessageType.Request,
            V = ProtocolConstants.Version,
            Id = "abc123",
            Name = ProtocolNames.Methods.DeviceConnect,
            TsUtc = DateTimeOffset.Parse("2026-04-01T18:00:00Z"),
            Payload = ProtocolJson.ToElement(new DeviceConnectRequest("COM7")),
        };

        var json = JsonSerializer.Serialize(envelope, ProtocolJson.SerializerOptions);

        Assert.Contains("\"type\":\"request\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"device.connect\"", json, StringComparison.Ordinal);
        Assert.Contains("\"portName\":\"COM7\"", json, StringComparison.Ordinal);

        var roundTrip = JsonSerializer.Deserialize<ProtocolEnvelope>(json, ProtocolJson.SerializerOptions);
        Assert.NotNull(roundTrip);
        Assert.Equal(ProtocolMessageType.Request, roundTrip!.Type);
        Assert.Equal("abc123", roundTrip.Id);

        var payload = ProtocolJson.DeserializePayload<DeviceConnectRequest>(roundTrip);
        Assert.Equal("COM7", payload.PortName);
    }
}
