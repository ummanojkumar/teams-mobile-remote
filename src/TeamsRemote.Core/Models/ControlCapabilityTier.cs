using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ControlCapabilityTier>))]
public enum ControlCapabilityTier
{
    [JsonPropertyName("verified")]
    Verified,

    [JsonPropertyName("observe-only")]
    ObserveOnly,

    [JsonPropertyName("unavailable")]
    Unavailable
}

public static class ControlCapabilityTierExtensions
{
    public static string ToWireString(this ControlCapabilityTier tier) => tier switch
    {
        ControlCapabilityTier.Verified => "verified",
        ControlCapabilityTier.ObserveOnly => "observe-only",
        ControlCapabilityTier.Unavailable => "unavailable",
        _ => "unavailable"
    };
}
