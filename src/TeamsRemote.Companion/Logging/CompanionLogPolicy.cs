using System;
using System.IO;

namespace TeamsRemote.Companion.Logging;

public sealed class CompanionLogPolicy
{
    private static CompanionLogPolicy? _current;
    private static readonly object CurrentLock = new();

    public static CompanionLogPolicy Current
    {
        get
        {
            if (_current == null)
            {
                lock (CurrentLock)
                {
                    _current ??= CreateDefault();
                }
            }
            return _current;
        }
        set
        {
            lock (CurrentLock)
            {
                _current = value;
            }
        }
    }

    public bool IsConsoleEnabled { get; set; }
    public bool IsDiagnosticMode { get; set; }
    public string LogDirectory { get; set; }

    public CompanionLogPolicy(string? logDirectory = null, bool isConsoleEnabled = false, bool isDiagnosticMode = false)
    {
        LogDirectory = logDirectory ?? GetDefaultLogDirectory();
        IsConsoleEnabled = isConsoleEnabled;
        IsDiagnosticMode = isDiagnosticMode;
    }

    public static string GetDefaultLogDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TeamsRemote", "logs");
    }

    public string GetCurrentLogFilePath()
    {
        return Path.Combine(LogDirectory, $"companion-{DateTime.UtcNow:yyyyMMdd}.log");
    }

    public static CompanionLogPolicy CreateDefault()
    {
        bool consoleRequested = false;
        bool diagRequested = false;

        string? envConsole = Environment.GetEnvironmentVariable("TEAMS_REMOTE_CONSOLE");
        if (string.Equals(envConsole, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(envConsole, "true", StringComparison.OrdinalIgnoreCase))
        {
            consoleRequested = true;
        }

        string? envDebug = Environment.GetEnvironmentVariable("TEAMS_REMOTE_DEBUG");
        if (string.Equals(envDebug, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(envDebug, "true", StringComparison.OrdinalIgnoreCase))
        {
            consoleRequested = true;
            diagRequested = true;
        }

        return new CompanionLogPolicy(
            logDirectory: GetDefaultLogDirectory(),
            isConsoleEnabled: consoleRequested,
            isDiagnosticMode: diagRequested);
    }

    public static CompanionLogPolicy InitializeFromArgs(string[]? args)
    {
        var policy = CreateDefault();

        if (args != null)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-c", StringComparison.OrdinalIgnoreCase))
                {
                    policy.IsConsoleEnabled = true;
                }
                else if (string.Equals(arg, "--diag", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(arg, "--diagnostic", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(arg, "--debug", StringComparison.OrdinalIgnoreCase))
                {
                    policy.IsConsoleEnabled = true;
                    policy.IsDiagnosticMode = true;
                }
            }
        }

        Current = policy;
        return policy;
    }
}
