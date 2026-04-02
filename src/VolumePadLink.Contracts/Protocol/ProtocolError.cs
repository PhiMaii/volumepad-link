using System.Text.Json.Serialization;

namespace VolumePadLink.Contracts.Protocol;

public sealed record ProtocolError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
