namespace TeamsRemote.Core.Models;

public record ReactionControlElementInfo
{
    public required string ReactionType { get; init; } // like, love, applause, laugh, surprise
    public required string AccessibleName { get; init; }
    public string? AutomationId { get; init; }
    public required string ControlType { get; init; }
    public bool SupportsInvokePattern { get; init; }
    public bool IsEnabled { get; init; }
    public string? BoundingRectangle { get; init; }
    public string? HelpText { get; init; }
}

public static class ReactionPolicyStatus
{
    public const string Enabled = "Enabled";
    public const string PolicyDisabled = "PolicyDisabled";
    public const string Unknown = "Unknown";
}

public record ReactionDiagnosticReport
{
    public bool MeetingDetected { get; init; }
    public bool ReactLauncherFound { get; init; }
    public string? LauncherName { get; init; }
    public string? LauncherAutomationId { get; init; }
    public string? LauncherControlType { get; init; }
    public bool LauncherSupportsExpandCollapse { get; init; }
    public bool LauncherIsEnabled { get; init; }
    public bool FlyoutObserved { get; init; }
    public string? FlyoutHostingType { get; init; } // "InTreeChild" vs "TopLevelPopup"
    public string PolicyStatus { get; init; } = ReactionPolicyStatus.Unknown;
    public string? PolicyReason { get; init; }
    public IReadOnlyList<ReactionControlElementInfo> DiscoveredReactions { get; init; } = Array.Empty<ReactionControlElementInfo>();
    public IReadOnlyList<string> ObservedEvents { get; init; } = Array.Empty<string>();
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

public record ReactionCommandResult
{
    public required CommandExecutionStatus Status { get; init; }
    public required string Reaction { get; init; }
    public string? ErrorCode { get; init; }
    public string? Reason { get; init; }
    public TimeSpan Duration { get; init; }
}
