using System;
using Microsoft.Extensions.Logging;

namespace TeamsRemote.Companion.Logging;

public static class CompanionLog
{
    public static void Info(string message, string category = "Companion")
    {
        CompanionLogWriter.Write(category, LogLevel.Information, message, null, CompanionLogPolicy.Current);
    }

    public static void Warn(string message, string category = "Companion")
    {
        CompanionLogWriter.Write(category, LogLevel.Warning, message, null, CompanionLogPolicy.Current);
    }

    public static void Error(string message, Exception? exception = null, string category = "Companion")
    {
        CompanionLogWriter.Write(category, LogLevel.Error, message, exception, CompanionLogPolicy.Current);
    }

    public static void Debug(string message, string category = "Companion")
    {
        CompanionLogWriter.Write(category, LogLevel.Debug, message, null, CompanionLogPolicy.Current);
    }
}
