using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Security;

public record PairedDeviceRecord
{
    [JsonPropertyName("deviceId")]
    public required Guid DeviceId { get; init; }

    [JsonPropertyName("deviceLabel")]
    public required string DeviceLabel { get; init; }

    [JsonPropertyName("pairedAt")]
    public DateTimeOffset PairedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sessionToken")]
    public required string SessionToken { get; init; }
}
