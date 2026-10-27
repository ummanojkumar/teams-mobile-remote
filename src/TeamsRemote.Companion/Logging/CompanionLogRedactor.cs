using System.Text.RegularExpressions;
using TeamsRemote.Core.Redaction;

namespace TeamsRemote.Companion.Logging;

public static class CompanionLogRedactor
{
    private static readonly Regex TokenRegex = new(
        @"(?<=[\s?&]token=|^token=)[a-zA-Z0-9_\-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PairingCodeRegex = new(
        @"(?<=[\s?&]code=|^code=)[a-zA-Z0-9]{4,8}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChallengeIdRegex = new(
        @"(?<=[\s?&]challengeId=|^challengeId=)[a-zA-Z0-9_\-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DeviceIdRegex = new(
        @"(?<=[\s?&]deviceId=|^deviceId=)[a-fA-F0-9\-]{36}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Redact(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // 1. Redact filesystem user profile paths
        string result = RedactionHelper.RedactFilePath(input);

        // 2. Redact standard secrets (Bearer tokens, JWTs)
        result = RedactionHelper.RedactSecrets(result);

        // 3. Redact URL / CLI / query string token and secret parameters
        result = TokenRegex.Replace(result, "[REDACTED_TOKEN]");
        result = PairingCodeRegex.Replace(result, "[REDACTED_CODE]");
        result = ChallengeIdRegex.Replace(result, "[REDACTED_CHALLENGE]");
        result = DeviceIdRegex.Replace(result, "[REDACTED_DEVICE_ID]");

        return result;
    }
}
