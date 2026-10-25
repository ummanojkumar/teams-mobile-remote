using TeamsRemote.Core.Models;

namespace TeamsRemote.Core.Classification;

public static class ControlCapabilityClassifier
{
    private static readonly HashSet<string> ActionablePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "TogglePattern",
        "Toggle",
        "InvokePattern",
        "Invoke",
        "SelectionItemPattern",
        "SelectionItem"
    };

    /// <summary>
    /// Classifies a control into Verified, ObserveOnly, or Unavailable according to architecture rules.
    /// In Phase 4B, Camera capability strictly remains ObserveOnly when readable.
    /// </summary>
    public static (ControlCapabilityTier Tier, string Reason) Classify(
        TargetControl targetControl,
        bool isFound,
        bool isEnabled,
        string readableState,
        IEnumerable<string> supportedPatterns)
    {
        if (!isFound)
        {
            return (ControlCapabilityTier.Unavailable, "Control element was not detected in Teams meeting UI.");
        }

        if (string.Equals(readableState, TeamsControlState.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            return (ControlCapabilityTier.Unavailable, "Control was detected but its state could not be determined.");
        }

        // Phase 4C Rule: Static classification for camera remains observe-only.
        // Promotion to 'verified' occurs dynamically in MeetingStateStore ONLY after a requested action
        // is independently verified through post-action UIA event or re-query.
        if (targetControl == TargetControl.Camera)
        {
            var isActionEligible = isEnabled && supportedPatterns.Any(p => ActionablePatterns.Contains(p));
            return (
                ControlCapabilityTier.ObserveOnly,
                isActionEligible
                    ? $"Camera state '{readableState}' is observable (observe-only, action-eligible). Capability promotes to verified upon confirmed post-action UIA verification."
                    : $"Camera state '{readableState}' is observable (observe-only). Action patterns unavailable or control disabled.");
        }

        var patterns = supportedPatterns.ToList();
        var presentActionablePatterns = patterns.Where(p => ActionablePatterns.Contains(p)).ToList();

        if (presentActionablePatterns.Count > 0 && isEnabled)
        {
            return (
                ControlCapabilityTier.Verified,
                $"State '{readableState}' is readable and actionable pattern [{string.Join(", ", presentActionablePatterns)}] is supported.");
        }

        if (!isEnabled)
        {
            return (
                ControlCapabilityTier.ObserveOnly,
                $"State '{readableState}' is readable, but control is currently disabled/read-only.");
        }

        return (
            ControlCapabilityTier.ObserveOnly,
            $"State '{readableState}' is readable, but no actionable execution pattern (Toggle/Invoke/SelectionItem) is supported.");
    }

    public static (ControlCapabilityTier Tier, string Reason) Classify(
        bool isFound,
        bool isEnabled,
        string readableState,
        IEnumerable<string> supportedPatterns) =>
        Classify(TargetControl.Microphone, isFound, isEnabled, readableState, supportedPatterns);

    public static bool IsActionEligible(
        bool isFound,
        bool isEnabled,
        string readableState,
        IEnumerable<string> supportedPatterns)
    {
        if (!isFound || !isEnabled) return false;
        if (string.Equals(readableState, TeamsControlState.Unknown, StringComparison.OrdinalIgnoreCase)) return false;
        return supportedPatterns.Any(p => ActionablePatterns.Contains(p));
    }

    /// <summary>
    /// Infers the canonical control state from accessible name, toggle state, and help text.
    /// </summary>
    public static (string State, string Source) ParseState(
        TargetControl targetControl,
        string? accessibleName,
        string? toggleState,
        string? helpText = null)
    {
        // 1. First priority: explicit accessible name phrasing (e.g. "Unmute microphone" = muted)
        if (!string.IsNullOrWhiteSpace(accessibleName))
        {
            var (stateFromName, isExplicit) = ParseFromAccessibleName(targetControl, accessibleName);
            if (isExplicit && stateFromName != TeamsControlState.Unknown)
            {
                return (stateFromName, "AccessibleName");
            }
        }

        // 2. Second priority: UIA TogglePattern state (On / Off / Indeterminate)
        if (!string.IsNullOrWhiteSpace(toggleState))
        {
            var stateFromToggle = ParseFromToggleState(targetControl, toggleState);
            if (stateFromToggle != TeamsControlState.Unknown)
            {
                return (stateFromToggle, "TogglePattern");
            }
        }

        // 3. Fallback: HelpText or tooltip phrasing
        if (!string.IsNullOrWhiteSpace(helpText))
        {
            var (stateFromHelp, _) = ParseFromAccessibleName(targetControl, helpText);
            if (stateFromHelp != TeamsControlState.Unknown)
            {
                return (stateFromHelp, "HelpText");
            }
        }

        return (TeamsControlState.Unknown, "None");
    }

    private static (string State, bool IsExplicit) ParseFromAccessibleName(TargetControl targetControl, string text)
    {
        string lower = text.ToLowerInvariant();

        switch (targetControl)
        {
            case TargetControl.Microphone:
                if (lower.Contains("unmute"))
                {
                    // Button action is "Unmute", meaning currently MUTED
                    return (TeamsControlState.Muted, true);
                }
                if (lower.Contains("mute") && !lower.Contains("unmute"))
                {
                    // Button action is "Mute", meaning currently UNMUTED
                    return (TeamsControlState.Unmuted, true);
                }
                break;

            case TargetControl.Camera:
                // Check "turn off" before "turn on" and "camera is on" before "camera is off"
                if (lower.Contains("turn camera off") || lower.Contains("turn off camera") ||
                    lower.Contains("stop video") || lower.Contains("camera is on") ||
                    lower.Contains("video enabled") || lower.Contains("video is on"))
                {
                    // Action is to turn off, or status is on -> currently ON
                    return (TeamsControlState.On, true);
                }
                if (lower.Contains("turn camera on") || lower.Contains("turn on camera") ||
                    lower.Contains("start video") || lower.Contains("camera is off") ||
                    lower.Contains("video disabled") || lower.Contains("video is off"))
                {
                    // Action is to turn on, or status is off -> currently OFF
                    return (TeamsControlState.Off, true);
                }
                break;

            case TargetControl.Hand:
                if (lower.Contains("lower hand") || lower.Contains("lower your hand"))
                {
                    // Action is to lower, so currently RAISED
                    return (TeamsControlState.Raised, true);
                }
                if (lower.Contains("raise hand") || lower.Contains("raise your hand"))
                {
                    // Action is to raise, so currently LOWERED
                    return (TeamsControlState.Lowered, true);
                }
                break;
        }

        return (TeamsControlState.Unknown, false);
    }

    private static string ParseFromToggleState(TargetControl targetControl, string toggleState)
    {
        string normalized = toggleState.Trim().ToLowerInvariant();

        return targetControl switch
        {
            TargetControl.Microphone => normalized switch
            {
                "on" => TeamsControlState.Unmuted,
                "off" => TeamsControlState.Muted,
                _ => TeamsControlState.Unknown
            },
            TargetControl.Camera => normalized switch
            {
                "on" => TeamsControlState.On,
                "off" => TeamsControlState.Off,
                _ => TeamsControlState.Unknown
            },
            TargetControl.Hand => normalized switch
            {
                "on" => TeamsControlState.Raised,
                "off" => TeamsControlState.Lowered,
                _ => TeamsControlState.Unknown
            },
            _ => TeamsControlState.Unknown
        };
    }
}
