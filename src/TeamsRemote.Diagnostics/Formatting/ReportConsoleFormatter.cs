using System.Text.Json;
using System.Text.Json.Serialization;
using TeamsRemote.Core.Models;

namespace TeamsRemote.Diagnostics.Formatting;

public static class ReportConsoleFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public static void PrintReport(TeamsDiagnosticReport report)
    {
        Console.WriteLine();
        PrintHeader("MICROSOFT TEAMS UI AUTOMATION FEASIBILITY REPORT (PHASE 0)");
        Console.WriteLine($" Timestamp (UTC): {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine(new string('-', 76));

        // 1. Process Section
        Console.WriteLine();
        PrintSectionTitle("1. TEAMS PROCESS STATUS");
        if (report.Process.IsRunning)
        {
            PrintStatusLine("Running", "YES", ConsoleColor.Green);
            Console.WriteLine($"  Process Name:    {report.Process.ProcessName}");
            Console.WriteLine($"  Process ID:      {report.Process.ProcessId}");
            Console.WriteLine($"  All Process IDs: [{string.Join(", ", report.Process.AllProcessIds)}]");
            if (!string.IsNullOrEmpty(report.Process.ExecutablePath))
            {
                Console.WriteLine($"  Executable Path: {report.Process.ExecutablePath}");
            }
        }
        else
        {
            PrintStatusLine("Running", "NO", ConsoleColor.Red);
            Console.WriteLine("  Microsoft Teams desktop app is not currently running.");
        }

        // 2. Meeting Surface Section
        Console.WriteLine();
        PrintSectionTitle("2. MEETING SURFACE DETECTION");
        if (report.MeetingSurface.IsDetected)
        {
            PrintStatusLine("Active Surface", "DETECTED", ConsoleColor.Green);
            Console.WriteLine($"  Window Handle:   0x{report.MeetingSurface.WindowHandle:X}");
            Console.WriteLine($"  Window Title:    {report.MeetingSurface.RedactedWindowTitle}");
            Console.WriteLine($"  Class Name:      {report.MeetingSurface.AutomationClassName}");
            Console.WriteLine($"  Surface Type:    {report.MeetingSurface.ControlSurfaceType}");
        }
        else
        {
            PrintStatusLine("Active Surface", "NOT DETECTED", ConsoleColor.Yellow);
            Console.WriteLine("  No active meeting window or in-call toolbar was detected.");
        }

        // 3. Meeting Controls Section
        Console.WriteLine();
        PrintSectionTitle("3. MEETING CONTROLS CAPABILITY MATRIX");

        foreach (var control in report.Controls)
        {
            Console.WriteLine();
            Console.WriteLine($" [{control.TargetControl.ToString().ToUpperInvariant()}]");

            Console.Write("  Found:               ");
            PrintColor(control.Found ? "Yes" : "No", control.Found ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine();

            Console.Write("  Capability Tier:     ");
            PrintTierBadge(control.CapabilityTier);
            Console.WriteLine();

            Console.WriteLine($"  Accessible Name:     {control.AccessibleName ?? "[Not Found]"}");
            Console.WriteLine($"  Control Type:        {control.ControlType ?? "[Not Found]"}");
            Console.WriteLine($"  Automation ID:       {control.AutomationId ?? "[None]"}");

            string patternsStr = control.SupportedPatterns.Count > 0
                ? string.Join(", ", control.SupportedPatterns)
                : "[None]";
            Console.WriteLine($"  Supported Patterns:  {patternsStr}");

            Console.Write("  Readable State:      ");
            PrintState(control.ReadableState);
            if (!string.IsNullOrEmpty(control.StateSource))
            {
                Console.Write($" (Source: {control.StateSource})");
            }
            Console.WriteLine();

            Console.Write("  Event Observable:    ");
            PrintColor(control.IsEventObservable ? "Yes" : "No", control.IsEventObservable ? ConsoleColor.Green : ConsoleColor.Yellow);
            Console.WriteLine();

            Console.WriteLine($"  Assessment:          {control.Reason}");
        }

        // 4. Summary & Feasibility Conclusion
        Console.WriteLine();
        PrintSectionTitle("4. DIAGNOSTIC SUMMARY");
        Console.WriteLine($"  {report.DiagnosticSummary}");

        bool allVerified = report.Controls.Count > 0 && report.Controls.All(c => c.CapabilityTier == ControlCapabilityTier.Verified);
        bool anyVerified = report.Controls.Any(c => c.CapabilityTier == ControlCapabilityTier.Verified);

        Console.WriteLine();
        Console.Write("  Phase 0 Gate Assessment: ");
        if (allVerified)
        {
            PrintColor("PASS — All controls verified for read and safe invocation", ConsoleColor.Green);
        }
        else if (anyVerified)
        {
            PrintColor("PARTIAL — Some controls verified, others observe-only or unavailable", ConsoleColor.Yellow);
        }
        else if (report.MeetingSurface.IsDetected)
        {
            PrintColor("BLOCKED — Active meeting detected but controls unverified", ConsoleColor.Red);
        }
        else
        {
            PrintColor("IDLE — Join or start a Teams meeting to evaluate live controls", ConsoleColor.Cyan);
        }
        Console.WriteLine();
        Console.WriteLine(new string('-', 76));
        Console.WriteLine();
    }

    public static void PrintJson(TeamsDiagnosticReport report)
    {
        string json = JsonSerializer.Serialize(report, JsonOptions);
        Console.WriteLine(json);
    }

    private static void PrintHeader(string title)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 76));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', 76));
        Console.ForegroundColor = prev;
    }

    private static void PrintSectionTitle(string title)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"--- {title} ---");
        Console.ForegroundColor = prev;
    }

    private static void PrintStatusLine(string label, string status, ConsoleColor color)
    {
        Console.Write($"  {label,-17}: ");
        PrintColor(status, color);
        Console.WriteLine();
    }

    private static void PrintTierBadge(ControlCapabilityTier tier)
    {
        var prev = Console.ForegroundColor;
        switch (tier)
        {
            case ControlCapabilityTier.Verified:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[VERIFIED]");
                break;
            case ControlCapabilityTier.ObserveOnly:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("[OBSERVE-ONLY]");
                break;
            case ControlCapabilityTier.Unavailable:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("[UNAVAILABLE]");
                break;
        }
        Console.ForegroundColor = prev;
    }

    private static void PrintState(string state)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = state switch
        {
            TeamsControlState.Unmuted or TeamsControlState.On or TeamsControlState.Raised => ConsoleColor.Green,
            TeamsControlState.Muted or TeamsControlState.Off or TeamsControlState.Lowered => ConsoleColor.DarkYellow,
            _ => ConsoleColor.Gray
        };
        Console.Write(state);
        Console.ForegroundColor = prev;
    }

    private static void PrintColor(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }
}
