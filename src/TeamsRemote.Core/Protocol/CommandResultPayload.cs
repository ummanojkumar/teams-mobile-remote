using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Protocol;

public record CommandResultPayload
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("stateRevision")]
    public long StateRevision { get; init; }

    [JsonPropertyName("state")]
    public IReadOnlyDictionary<string, string>? State { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("observedAt")]
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}
