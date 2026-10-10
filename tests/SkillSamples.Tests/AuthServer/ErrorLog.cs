using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SkillSamples.AuthServer;

// Keeps the server's errors, so a failed test can say what went wrong inside it.
public sealed class ErrorLog : ILoggerProvider
{
    public ConcurrentQueue<string> Errors { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    public override string ToString() => string.Join(Environment.NewLine, Errors);

    private sealed class Logger(ErrorLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                log.Errors.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
