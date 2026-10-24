using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<DesiredMicrophoneState>))]
public enum DesiredMicrophoneState
{
    [JsonPropertyName("muted")]
    Muted,

    [JsonPropertyName("unmuted")]
    Unmuted
}

public static class DesiredMicrophoneStateExtensions
{
    public static string ToWireString(this DesiredMicrophoneState state) => state switch
    {
        DesiredMicrophoneState.Muted => TeamsControlState.Muted,
        DesiredMicrophoneState.Unmuted => TeamsControlState.Unmuted,
        _ => TeamsControlState.Unknown
    };
}
