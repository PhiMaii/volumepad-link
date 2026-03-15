using System.Text.Json;

namespace VolumePadLink.Agent.Services.Interfaces;

public interface IDeviceProtocolCodec
{
    bool TryParseEnvelope(string line, out DeviceProtocolEnvelope? envelope, out string? error);

    string Encode(string type, object? payload = null, string? requestId = null);
}

public sealed record DeviceProtocolEnvelope(string Type, string? RequestId, JsonElement Payload);
