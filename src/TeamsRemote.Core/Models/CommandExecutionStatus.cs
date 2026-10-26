using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<CommandExecutionStatus>))]
public enum CommandExecutionStatus
{
    [JsonPropertyName("confirmed")]
    Confirmed,

    [JsonPropertyName("already_in_desired_state")]
    AlreadyInDesiredState,

    [JsonPropertyName("failed")]
    Failed,

    [JsonPropertyName("timed_out")]
    TimedOut,

    [JsonPropertyName("unsupported")]
    Unsupported,

    [JsonPropertyName("rejected")]
    Rejected,

    [JsonPropertyName("state_uncertain")]
    StateUncertain
}
