using System.Text.RegularExpressions;
using TeamsRemote.Core.Models;

namespace TeamsRemote.Core.Redaction;

public static class RedactionHelper
{
    private static readonly Regex TeamsPipeRegex = new(
        @"^.+?(\s*\|\s*Microsoft Teams.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UserProfilePathRegex = new(
        @"([a-zA-Z]:\\Users\\)[^\\]+(\\.*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerTokenRegex = new(
        @"Bearer\s+[a-zA-Z0-9\-_.]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GenericSecretRegex = new(
        @"(eyJ[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_-]{10,})",
        RegexOptions.Compiled);

    /// <summary>
    /// Redacts meeting titles from window titles while preserving standard Microsoft Teams markers.
    /// E.g. "Design Review (Confidential) | Microsoft Teams" -> "[REDACTED_MEETING_TITLE] | Microsoft Teams"
    /// </summary>
    public static string RedactWindowTitle(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return "[EMPTY_OR_UNTITLED]";
        }

        string trimmed = windowTitle.Trim();

        // Exact standard window title
        if (string.Equals(trimmed, "Microsoft Teams", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Microsoft Teams (Work or school)", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        // Teams window with pipe: "<Meeting Title> | Microsoft Teams..."
        var match = TeamsPipeRegex.Match(trimmed);
        if (match.Success)
        {
            return $"[REDACTED_MEETING_TITLE]{match.Groups[1].Value}";
        }

        // If it doesn't match standard patterns, redact the entire window title to prevent leaks
        return "[REDACTED_WINDOW_TITLE]";
    }

    /// <summary>
    /// Redacts user profile names from filesystem paths (e.g. C:\Users\john\...).
    /// </summary>
    public static string RedactFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return UserProfilePathRegex.Replace(path, "$1[REDACTED]$2");
    }

    /// <summary>
    /// Redacts sensitive strings (tokens, secrets, bearer auth headers).
    /// </summary>
    public static string RedactSecrets(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = BearerTokenRegex.Replace(text, "Bearer [REDACTED_TOKEN]");
        cleaned = GenericSecretRegex.Replace(cleaned, "[REDACTED_JWT]");
        return cleaned;
    }

    /// <summary>
    /// Sanitizes an accessible name to ensure no participant names or raw chat previews leak.
    /// Known safe control names (and hotkeys) are preserved.
    /// </summary>
    public static string SanitizeAccessibleName(string? accessibleName, TargetControl targetControl)
    {
        if (string.IsNullOrWhiteSpace(accessibleName))
        {
            return "[UNNAMED]";
        }

        string trimmed = accessibleName.Trim();

        // Check if it starts with or matches expected control semantics
        bool isKnownControlPattern = targetControl switch
        {
            TargetControl.Microphone =>
                trimmed.Contains("mic", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("mute", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("audio", StringComparison.OrdinalIgnoreCase),

            TargetControl.Camera =>
                trimmed.Contains("camera", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("video", StringComparison.OrdinalIgnoreCase),

            TargetControl.Hand =>
                trimmed.Contains("hand", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("react", StringComparison.OrdinalIgnoreCase),

            _ => false
        };

        if (isKnownControlPattern)
        {
            return RedactSecrets(trimmed);
        }

        // Unrecognized name for this control type - redact to avoid leaking chat or attendee name
        return "[REDACTED_CONTROL_LABEL]";
    }

    /// <summary>
    /// Applies full sanitization across a diagnostic report.
    /// </summary>
    public static TeamsDiagnosticReport SanitizeReport(TeamsDiagnosticReport report)
    {
        var sanitizedProcess = report.Process with
        {
            ExecutablePath = RedactFilePath(report.Process.ExecutablePath)
        };

        var sanitizedSurface = report.MeetingSurface with
        {
            RedactedWindowTitle = RedactWindowTitle(report.MeetingSurface.RedactedWindowTitle)
        };

        var sanitizedControls = report.Controls.Select(c => c with
        {
            AccessibleName = SanitizeAccessibleName(c.AccessibleName, c.TargetControl),
            Reason = RedactSecrets(RedactFilePath(c.Reason))
        }).ToList();

        return report with
        {
            Process = sanitizedProcess,
            MeetingSurface = sanitizedSurface,
            Controls = sanitizedControls,
            DiagnosticSummary = RedactSecrets(RedactFilePath(report.DiagnosticSummary))
        };
    }
}
