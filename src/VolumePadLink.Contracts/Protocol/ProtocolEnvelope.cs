using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolumePadLink.Contracts.Protocol;

public sealed class ProtocolEnvelope
{
    [JsonPropertyName("v")]
    public int V { get; init; } = ProtocolConstants.Version;

    [JsonPropertyName("type")]
    public ProtocolMessageType Type { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("tsUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? TsUtc { get; init; }

    [JsonPropertyName("ok")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Ok { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProtocolError? Error { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; } = ProtocolJson.EmptyObject;
}
