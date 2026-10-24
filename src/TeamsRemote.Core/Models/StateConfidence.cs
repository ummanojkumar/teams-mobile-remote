using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<StateConfidence>))]
public enum StateConfidence
{
    [JsonPropertyName("confirmed")]
    Confirmed,

    [JsonPropertyName("pending")]
    Pending,

    [JsonPropertyName("stale")]
    Stale,

    [JsonPropertyName("unknown")]
    Unknown
}

public static class StateConfidenceExtensions
{
    public static string ToWireString(this StateConfidence confidence) => confidence switch
    {
        StateConfidence.Confirmed => "confirmed",
        StateConfidence.Pending => "pending",
        StateConfidence.Stale => "stale",
        StateConfidence.Unknown => "unknown",
        _ => "unknown"
    };
}
