using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Protocol;

public record PairChallengePayload
{
    [JsonPropertyName("challengeId")]
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid ChallengeId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("relayUrl")]
    public string RelayUrl { get; init; } = string.Empty;

    [JsonPropertyName("shortCode")]
    public string ShortCode { get; init; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; }

    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}

public record PairRequestPayload
{
    [JsonPropertyName("challengeId")]
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid ChallengeId { get; init; }

    [JsonPropertyName("deviceLabel")]
    public string DeviceLabel { get; init; } = "Mobile Device";

    [JsonPropertyName("shortCode")]
    public string ShortCode { get; init; } = string.Empty;
}

public record PairApprovePayload
{
    [JsonPropertyName("challengeId")]
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid ChallengeId { get; init; }

    [JsonPropertyName("approved")]
    public bool Approved { get; init; }

    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; init; }

    [JsonPropertyName("deviceId")]
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid DeviceId { get; init; }
}

public record RevokePayload
{
    [JsonPropertyName("deviceId")]
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid DeviceId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
