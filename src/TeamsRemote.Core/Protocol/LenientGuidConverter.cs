using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamsRemote.Core.Protocol;

/// <summary>
/// Tolerant Guid converter for System.Text.Json.
/// Parses valid GUID strings (RFC 4122), handles empty/null strings as Guid.Empty,
/// and deterministically hashes arbitrary non-GUID strings (e.g. from non-secure mobile web contexts)
/// into standard GUIDs instead of throwing a deserialization exception.
/// </summary>
public class LenientGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return Guid.Empty;
            }

            if (Guid.TryParse(str, out var parsedGuid))
            {
                return parsedGuid;
            }

            // Fallback for non-standard GUID strings:
            // Deterministically hash the string to a valid Guid so message deduplication and correlation still work.
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(str));
            return new Guid(hash);
        }

        return Guid.Empty;
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
