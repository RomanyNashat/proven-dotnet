using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace SkillSamples.Diagnostics;

public sealed record LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Fields);

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public readonly ConcurrentQueue<LogEntry> Entries = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }

    private sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?>();
            owner.Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), fields));
        }
    }
}

[Collection("heartbeat")]   // the exception counter sees the whole process; keep these apart
public sealed class RuntimeHeartbeatTests
{
    private static (IHost Host, FakeTimeProvider Time, CapturingLoggerProvider Logs) Build(long queueWarning = 100)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-10T00:00:00Z"));
        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.Configure<RuntimeHeartbeatOptions>(o => o.ThreadPoolQueueWarning = queueWarning);
        builder.Services.AddHostedService<RuntimeHeartbeat>();
        return (builder.Build(), time, logs);
    }

    private static async Task<LogEntry> Heartbeat(FakeTimeProvider time, CapturingLoggerProvider logs, int number = 1)
    {
        // The timer is created after the host starts, so keep moving time until the line appears.
        for (var i = 0; i < 100; i++)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(20);
            var lines = logs.Entries.Where(e => e.Message.StartsWith("Runtime heartbeat", StringComparison.Ordinal)).ToList();
            if (lines.Count >= number)
                return lines[number - 1];
        }
        throw new TimeoutException($"heartbeat line {number} was never logged");
    }

    [Fact]
    public async Task Heartbeat_OneInterval_LogsTheRuntimeCountersAsFields()
    {
        var (host, time, logs) = Build();
        await host.StartAsync();
        try
        {
            var line = await Heartbeat(time, logs);

            Assert.Equal(LogLevel.Information, line.Level);
            foreach (var field in new[] { "ThreadPoolThreads", "ThreadPoolQueue", "WorkItemsPerSecond", "CpuPercent",
                         "WorkingSetMb", "HeapMb", "FragmentedMb", "MemoryLimitMb", "Gen0", "Gen1", "Gen2",
                         "GcPauseMs", "LockContentions", "Exceptions" })
                Assert.True(line.Fields.ContainsKey(field), $"missing field {field}");
            Assert.True(Convert.ToInt64(line.Fields["WorkingSetMb"]) > 0);
            Assert.True(Convert.ToInt32(line.Fields["ThreadPoolThreads"]) > 0);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Heartbeat_QueueAboveTheThreshold_LogsAWarning()
    {
        var (host, time, logs) = Build(queueWarning: -1);   // any queue length is above -1
        await host.StartAsync();
        try
        {
            var line = await Heartbeat(time, logs);
            Assert.Equal(LogLevel.Warning, line.Level);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Heartbeat_HandledExceptions_AreCounted()
    {
        var (host, time, logs) = Build();
        await host.StartAsync();
        try
        {
            await Heartbeat(time, logs);   // after the first line the counter is subscribed
            for (var i = 0; i < 5; i++)
            {
                try { throw new InvalidOperationException("handled"); }
                catch (InvalidOperationException) { }
            }

            var line = await Heartbeat(time, logs, number: 2);
            var counted = Convert.ToInt64(line.Fields["Exceptions"]);
            Assert.True(counted >= 5, $"counted {counted}");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
