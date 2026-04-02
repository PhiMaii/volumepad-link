using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolumePadLink.Contracts.Protocol;

public static class ProtocolJson
{
    private static readonly JsonDocument EmptyObjectDocument = JsonDocument.Parse("{}");

    public static JsonElement EmptyObject => EmptyObjectDocument.RootElement.Clone();

    public static JsonSerializerOptions SerializerOptions { get; } = BuildSerializerOptions();

    public static JsonElement ToElement<T>(T payload)
    {
        return JsonSerializer.SerializeToElement(payload, SerializerOptions);
    }

    public static T DeserializePayload<T>(ProtocolEnvelope envelope)
    {
        var value = envelope.Payload.Deserialize<T>(SerializerOptions);
        if (value is null)
        {
            throw new InvalidOperationException($"Payload for '{envelope.Name}' is missing or invalid.");
        }

        return value;
    }

    private static JsonSerializerOptions BuildSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
