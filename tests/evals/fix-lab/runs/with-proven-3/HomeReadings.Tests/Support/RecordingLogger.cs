using Microsoft.Extensions.Logging;

namespace HomeReadings.Tests.Support;

public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly TaskCompletionSource _firstError = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public Task FirstError => _firstError.Task;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        if (logLevel >= LogLevel.Error)
        {
            _firstError.TrySetResult();
        }
    }
}
