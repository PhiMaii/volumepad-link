using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolumePadLink.Contracts.Abstractions;

public static class IpcMessageKinds
{
    public const string Command = "command";
    public const string Response = "response";
    public const string Event = "event";
}

public sealed record IpcMessage
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
