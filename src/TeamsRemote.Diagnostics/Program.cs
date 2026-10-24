using TeamsRemote.Core.Abstractions;
using TeamsRemote.Diagnostics.Adapter;
using TeamsRemote.Diagnostics.Formatting;

namespace TeamsRemote.Diagnostics;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool outputJson = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-j", StringComparison.OrdinalIgnoreCase));
        bool isObserveMode = args.Any(a => string.Equals(a, "observe", StringComparison.OrdinalIgnoreCase));
        bool showHelp = args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase));

        if (showHelp)
        {
            PrintHelp();
            return 0;
        }

        using ITeamsController controller = new UiaTeamsController();

        if (isObserveMode)
        {
            int durationSeconds = 30;
            int durIndex = Array.FindIndex(args, a => string.Equals(a, "--duration", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-d", StringComparison.OrdinalIgnoreCase));
            if (durIndex >= 0 && durIndex + 1 < args.Length && int.TryParse(args[durIndex + 1], out int parsedDuration))
            {
                durationSeconds = Math.Clamp(parsedDuration, 5, 300);
            }

            Console.WriteLine();
            Console.WriteLine($"Starting passive state-change observation for {durationSeconds} seconds...");
            Console.WriteLine("Press Ctrl+C to cancel at any time. No actions or clicks will be sent to Teams.");
            Console.WriteLine();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            int eventsCount = 0;
            await controller.ObserveEventsAsync(
                TimeSpan.FromSeconds(durationSeconds),
                evt =>
                {
                    Interlocked.Increment(ref eventsCount);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"[{evt.ObservedAt:HH:mm:ss.fff}] ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(evt.RedactedSummary);
                    Console.ResetColor();
                },
                cts.Token);

            Console.WriteLine();
            Console.WriteLine($"Observation finished. Total state-change events observed: {eventsCount}.");
            return 0;
        }

        // Default: Run inspection report
        var report = await controller.InspectAsync();

        if (outputJson)
        {
            ReportConsoleFormatter.PrintJson(report);
        }
        else
        {
            ReportConsoleFormatter.PrintReport(report);
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Teams Mobile Remote — Diagnostic Tool (Phase 0 Feasibility Spike)");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run [command] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  report (default)     Inspects Teams desktop and prints capability matrix");
        Console.WriteLine("  observe              Passively listens for state-change events without clicking");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --json, -j           Output diagnostic report as formatted JSON");
        Console.WriteLine("  --duration, -d <sec> Duration in seconds for observation mode (default: 30)");
        Console.WriteLine("  --help, -h           Show this help information");
        Console.WriteLine();
    }
}
