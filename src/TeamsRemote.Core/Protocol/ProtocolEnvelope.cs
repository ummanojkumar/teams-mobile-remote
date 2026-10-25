using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Protocol;

public record ProtocolEnvelope
{
    public const int CurrentVersion = 1;
    public const int MaxPayloadSizeBytes = 16 * 1024; // 16 KB

    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("messageId")]
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid MessageId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("correlationId")]
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("deviceId")]
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid DeviceId { get; init; } = Guid.Empty;

    [JsonPropertyName("sentAt")]
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }

    public static ProtocolEnvelope Create<T>(
        string type,
        T payload,
        Guid deviceId,
        Guid? correlationId = null,
        Guid? messageId = null)
    {
        string json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        return new ProtocolEnvelope
        {
            Version = CurrentVersion,
            Type = type,
            MessageId = messageId ?? Guid.NewGuid(),
            CorrelationId = correlationId ?? Guid.NewGuid(),
            DeviceId = deviceId,
            SentAt = DateTimeOffset.UtcNow,
            Payload = doc.RootElement.Clone()
        };
    }

    public T? DeserializePayload<T>(JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(Payload.GetRawText(), options);
    }

    public string Serialize()
    {
        string json = JsonSerializer.Serialize(this);
        int byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaxPayloadSizeBytes)
        {
            throw new InvalidOperationException($"Message size ({byteCount} bytes) exceeds maximum allowable limit of {MaxPayloadSizeBytes} bytes.");
        }
        return json;
    }

    public static (bool Success, ProtocolEnvelope? Envelope, string? ErrorCode, string? ErrorReason) TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (false, null, "EMPTY_MESSAGE", "Message is null or empty.");
        }

        int byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaxPayloadSizeBytes)
        {
            return (false, null, ProtocolErrorCodes.PayloadTooLarge, $"Message size ({byteCount} bytes) exceeds {MaxPayloadSizeBytes} bytes limit.");
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(json);
            if (envelope == null)
            {
                return (false, null, "MALFORMED_JSON", "Could not parse JSON into envelope.");
            }

            if (envelope.Version != CurrentVersion)
            {
                return (false, envelope, ProtocolErrorCodes.UnsupportedProtocolVersion, $"Envelope version {envelope.Version} is unsupported. Current supported version is {CurrentVersion}.");
            }

            return (true, envelope, null, null);
        }
        catch (JsonException ex)
        {
            return (false, null, "MALFORMED_JSON", ex.Message);
        }
    }
}
