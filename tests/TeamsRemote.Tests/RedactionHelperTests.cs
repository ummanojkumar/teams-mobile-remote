using TeamsRemote.Core.Models;
using TeamsRemote.Core.Redaction;
using Xunit;

namespace TeamsRemote.Tests;

public class RedactionHelperTests
{
    [Theory]
    [InlineData("Microsoft Teams", "Microsoft Teams")]
    [InlineData("Microsoft Teams (Work or school)", "Microsoft Teams (Work or school)")]
    public void RedactWindowTitle_StandardWindowTitles_PreservesGenericName(string input, string expected)
    {
        var result = RedactionHelper.RedactWindowTitle(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Weekly All-Hands Sync | Microsoft Teams", "[REDACTED_MEETING_TITLE] | Microsoft Teams")]
    [InlineData("Project Titan Architecture (Confidential) | Microsoft Teams", "[REDACTED_MEETING_TITLE] | Microsoft Teams")]
    [InlineData("Chat with John Doe | Microsoft Teams (Work or school)", "[REDACTED_MEETING_TITLE] | Microsoft Teams (Work or school)")]
    public void RedactWindowTitle_WithMeetingOrChatTitle_RedactsTitle(string input, string expected)
    {
        var result = RedactionHelper.RedactWindowTitle(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RedactWindowTitle_ArbitraryOrEmpty_RedactsSafely()
    {
        Assert.Equal("[EMPTY_OR_UNTITLED]", RedactionHelper.RedactWindowTitle("   "));
        Assert.Equal("[REDACTED_WINDOW_TITLE]", RedactionHelper.RedactWindowTitle("Top Secret Document Preview"));
    }

    [Fact]
    public void RedactFilePath_MasksUserProfileDirectory()
    {
        string path = @"C:\Users\JohnDoe\AppData\Local\Microsoft\Teams\current\Teams.exe";
        string result = RedactionHelper.RedactFilePath(path);

        Assert.Equal(@"C:\Users\[REDACTED]\AppData\Local\Microsoft\Teams\current\Teams.exe", result);
        Assert.DoesNotContain("JohnDoe", result);
    }

    [Fact]
    public void RedactSecrets_MasksBearerTokens()
    {
        string input = "Request with Bearer abcdef1234567890 token";
        string result = RedactionHelper.RedactSecrets(input);

        Assert.DoesNotContain("abcdef1234567890", result);
        Assert.Contains("Bearer [REDACTED_TOKEN]", result);
    }

    [Fact]
    public void RedactSecrets_MasksStandaloneJwt()
    {
        string input = "Token is eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.doNotLeakThisSignature";
        string result = RedactionHelper.RedactSecrets(input);

        Assert.DoesNotContain("doNotLeakThisSignature", result);
        Assert.Contains("[REDACTED_JWT]", result);
    }

    [Fact]
    public void SanitizeAccessibleName_ValidControlPhrases_Preserved()
    {
        Assert.Equal("Mute microphone (Ctrl+Shift+M)",
            RedactionHelper.SanitizeAccessibleName("Mute microphone (Ctrl+Shift+M)", TargetControl.Microphone));

        Assert.Equal("Turn camera off",
            RedactionHelper.SanitizeAccessibleName("Turn camera off", TargetControl.Camera));

        Assert.Equal("Raise hand",
            RedactionHelper.SanitizeAccessibleName("Raise hand", TargetControl.Hand));
    }

    [Fact]
    public void SanitizeAccessibleName_SuspiciousChatOrParticipantText_Redacted()
    {
        Assert.Equal("[REDACTED_CONTROL_LABEL]",
            RedactionHelper.SanitizeAccessibleName("Jane Doe: Let's meet at 5pm", TargetControl.Microphone));

        Assert.Equal("[REDACTED_CONTROL_LABEL]",
            RedactionHelper.SanitizeAccessibleName("Meeting Chat - 12 new messages", TargetControl.Camera));
    }

    [Fact]
    public void SanitizeReport_RedactsAllSensitiveFields()
    {
        var report = new TeamsDiagnosticReport
        {
            Process = new TeamsProcessInfo
            {
                IsRunning = true,
                ProcessName = "ms-teams",
                ProcessId = 1234,
                ExecutablePath = @"C:\Users\SecretUser\AppData\Local\ms-teams.exe"
            },
            MeetingSurface = new MeetingSurfaceInfo
            {
                IsDetected = true,
                WindowHandle = 0x1234,
                RedactedWindowTitle = "Secret Acquisition Meeting | Microsoft Teams",
                AutomationClassName = "Chrome_WidgetWin_1",
                ControlSurfaceType = "MeetingToolbar"
            },
            Controls = new[]
            {
                new ControlDiagnosticInfo
                {
                    TargetControl = TargetControl.Microphone,
                    Found = true,
                    AccessibleName = "Mute microphone",
                    ControlType = "Button",
                    CapabilityTier = ControlCapabilityTier.Verified,
                    Reason = "Bearer super_secret_token"
                }
            },
            DiagnosticSummary = "Inspected C:\\Users\\SecretUser\\test"
        };

        var sanitized = RedactionHelper.SanitizeReport(report);

        Assert.DoesNotContain("SecretUser", sanitized.Process.ExecutablePath);
        Assert.Contains("[REDACTED]", sanitized.Process.ExecutablePath);

        Assert.DoesNotContain("Secret Acquisition Meeting", sanitized.MeetingSurface.RedactedWindowTitle);
        Assert.Contains("[REDACTED_MEETING_TITLE]", sanitized.MeetingSurface.RedactedWindowTitle);

        Assert.DoesNotContain("super_secret_token", sanitized.Controls[0].Reason);
        Assert.DoesNotContain("SecretUser", sanitized.DiagnosticSummary);
    }
}
