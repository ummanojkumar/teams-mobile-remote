using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TeamsRemote.Companion.Logging;

public sealed class CompanionLoggerProvider : ILoggerProvider
{
    private readonly CompanionLogPolicy _policy;
    private readonly ConcurrentDictionary<string, CompanionFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public CompanionLoggerProvider(CompanionLogPolicy? policy = null)
    {
        _policy = policy ?? CompanionLogPolicy.Current;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, cat => new CompanionFileLogger(cat, _policy));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}

public sealed class CompanionFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly CompanionLogPolicy _policy;

    public CompanionFileLogger(string categoryName, CompanionLogPolicy policy)
    {
        _categoryName = categoryName;
        _policy = policy;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None) return false;
        if (!_policy.IsDiagnosticMode && logLevel < LogLevel.Information) return false;
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        if (formatter == null) return;

        string message = formatter(state, exception);
        CompanionLogWriter.Write(_categoryName, logLevel, message, exception, _policy);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
