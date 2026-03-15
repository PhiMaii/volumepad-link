using System.Text.Json;
using VolumePadLink.Agent.Services.Interfaces;

namespace VolumePadLink.Agent.Services.Device;

public sealed class DeviceProtocolCodec : IDeviceProtocolCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool TryParseEnvelope(string line, out DeviceProtocolEnvelope? envelope, out string? error)
    {
        envelope = null;
        error = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "Line is empty.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Message root must be an object.";
                return false;
            }

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                error = "Missing string property 'type'.";
                return false;
            }

            var type = typeElement.GetString();
            if (string.IsNullOrWhiteSpace(type))
            {
                error = "Type cannot be empty.";
                return false;
            }

            string? requestId = null;
            if (root.TryGetProperty("requestId", out var requestIdElement) && requestIdElement.ValueKind == JsonValueKind.String)
            {
                requestId = requestIdElement.GetString();
            }

            var payload = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement.Clone()
                : JsonSerializer.SerializeToElement(new { });

            envelope = new DeviceProtocolEnvelope(type, requestId, payload);
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public string Encode(string type, object? payload = null, string? requestId = null)
    {
        var message = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["payload"] = payload ?? new { }
        };

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            message["requestId"] = requestId;
        }

        return JsonSerializer.Serialize(message, JsonOptions);
    }
}
