using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Protocol;

public record CommandPayload
{
    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("desired")]
    public string? Desired { get; init; }

    [JsonPropertyName("expectedStateRevision")]
    public long? ExpectedStateRevision { get; init; }

    [JsonPropertyName("echoText")]
    public string? EchoText { get; init; }

    [JsonPropertyName("reaction")]
    public string? Reaction { get; init; }
}
