using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TeamsRemote.Companion.Logging;

public static class CompanionLogWriter
{
    private static readonly object WriteLock = new();
    private static DateTime _lastPruneUtc = DateTime.MinValue;

    public static void Write(string category, LogLevel level, string message, Exception? exception, CompanionLogPolicy policy)
    {
        string redactedMessage = CompanionLogRedactor.Redact(message);
        string? redactedEx = exception != null ? CompanionLogRedactor.Redact(exception.ToString()) : null;

        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string levelStr = level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => "LOG"
        };

        string logLine = $"[{timestamp}] [{levelStr}] [{category}] {redactedMessage}";
        if (redactedEx != null)
        {
            logLine += Environment.NewLine + redactedEx;
        }

        // If console is enabled (development / diag mode), output to Console
        if (policy.IsConsoleEnabled)
        {
            try
            {
                var prevColor = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Information => ConsoleColor.Cyan,
                    _ => ConsoleColor.Gray
                };
                Console.WriteLine(logLine);
                Console.ForegroundColor = prevColor;
            }
            catch
            {
                // Ignore console writing failures if console handle is invalid
            }
        }

        // Write to thread-safe rolling log file in %LOCALAPPDATA%\TeamsRemote\logs\
        lock (WriteLock)
        {
            try
            {
                if (!Directory.Exists(policy.LogDirectory))
                {
                    Directory.CreateDirectory(policy.LogDirectory);
                }

                string targetFilePath = policy.GetCurrentLogFilePath();
                File.AppendAllText(targetFilePath, logLine + Environment.NewLine, Encoding.UTF8);

                PruneOldLogs(policy.LogDirectory, maxAgeDays: 7);
            }
            catch
            {
                // Logging failures must never crash the companion application
            }
        }
    }

    public static void PruneOldLogs(string logDirectory, int maxAgeDays = 7)
    {
        // Prune at most once per 30 minutes to minimize disk operations
        if (DateTime.UtcNow - _lastPruneUtc < TimeSpan.FromMinutes(30))
        {
            return;
        }
        _lastPruneUtc = DateTime.UtcNow;

        try
        {
            var dir = new DirectoryInfo(logDirectory);
            if (!dir.Exists) return;

            var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
            foreach (var file in dir.GetFiles("companion-*.log"))
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // File may be held open or locked
                    }
                }
            }
        }
        catch
        {
            // Suppress cleanup errors
        }
    }
}
