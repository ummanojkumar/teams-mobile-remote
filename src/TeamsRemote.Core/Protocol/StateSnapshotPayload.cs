using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Protocol;

public record ControlSnapshotItem
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("capability")]
    public required string Capability { get; init; }

    [JsonPropertyName("confidence")]
    public required string Confidence { get; init; }
}

public record StateSnapshotPayload
{
    [JsonPropertyName("stateRevision")]
    public long StateRevision { get; init; }

    [JsonPropertyName("meeting")]
    public string Meeting { get; init; } = "none";

    [JsonPropertyName("controls")]
    public IReadOnlyDictionary<string, ControlSnapshotItem> Controls { get; init; } = new Dictionary<string, ControlSnapshotItem>();

    [JsonPropertyName("connection")]
    public string Connection { get; init; } = "connected";

    [JsonPropertyName("observedAt")]
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}
