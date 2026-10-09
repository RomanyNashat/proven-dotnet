using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace SkillSamples.Workers;

public sealed class SlowSender : INotificationSender
{
    public static readonly ConcurrentBag<int> Sent = [];
    public static TimeSpan Delay = TimeSpan.FromMilliseconds(20);

    public async Task SendAsync(NotificationMessage message, CancellationToken ct)
    {
        await Task.Delay(Delay, ct);
        Sent.Add(message.Id);
    }
}

// The version the skill used to show: base.StopAsync first, which cancels the loop with items still queued.
public sealed class NaiveDispatchWorker(NotificationQueue queue, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<INotificationSender>().SendAsync(message, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }
}

public sealed class CountingJob : ISyncJob
{
    public int Runs;
    public readonly SemaphoreSlim Ran = new(0);

    public Task RunAsync(CancellationToken ct)
    {
        var run = Interlocked.Increment(ref Runs);
        Ran.Release();
        return run == 2 ? throw new InvalidOperationException("second run fails") : Task.CompletedTask;
    }
}

public sealed class FailingWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        throw new InvalidOperationException("boom");
    }
}

[Collection("workers")]   // SlowSender is shared state; run these one at a time
public sealed class WorkerTests
{
    private static IHost QueueHost<TWorker>() where TWorker : class, IHostedService
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<NotificationQueue>();
        builder.Services.AddScoped<INotificationSender, SlowSender>();
        builder.Services.AddHostedService<TWorker>();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));
        return builder.Build();
    }

    private static async Task<int> SentAfterShutdown<TWorker>() where TWorker : class, IHostedService
    {
        SlowSender.Sent.Clear();
        using var host = QueueHost<TWorker>();
        await host.StartAsync();
        var queue = host.Services.GetRequiredService<NotificationQueue>();
        for (var i = 1; i <= 20; i++)
        {
            await queue.EnqueueAsync(new NotificationMessage(i, "Your appointment is tomorrow"), default);
        }

        await host.StopAsync();
        return SlowSender.Sent.Count;
    }

    [Fact]
    public async Task Shutdown_DrainsTheQueueFirst() => Assert.Equal(20, await SentAfterShutdown<NotificationDispatchWorker>());

    [Fact]
    public async Task Shutdown_NaiveVersion_LosesQueuedItems() => Assert.True(await SentAfterShutdown<NaiveDispatchWorker>() < 20);

    [Fact]
    public async Task Shutdown_DrainStopsAtTheShutdownTimeout()
    {
        SlowSender.Sent.Clear();
        SlowSender.Delay = TimeSpan.FromSeconds(30);
        try
        {
            using var host = QueueHost<NotificationDispatchWorker>();
            await host.StartAsync();
            await host.Services.GetRequiredService<NotificationQueue>().EnqueueAsync(new(1, "x"), default);

            using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await host.StopAsync(shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                // the host may report the cut-short shutdown; what matters is how long it took
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
            Assert.Empty(SlowSender.Sent);
        }
        finally
        {
            SlowSender.Delay = TimeSpan.FromMilliseconds(20);
        }
    }

    [Fact]
    public async Task Queue_AfterShutdown_RefusesNewItems()
    {
        using var host = QueueHost<NotificationDispatchWorker>();
        await host.StartAsync();
        await host.StopAsync();

        await Assert.ThrowsAsync<System.Threading.Channels.ChannelClosedException>(async () =>
            await host.Services.GetRequiredService<NotificationQueue>().EnqueueAsync(new(1, "x"), default));
    }

    [Fact]
    public async Task Periodic_RunsAtStartThenEachTick_AndSurvivesAFailedRun()
    {
        var time = new FakeTimeProvider();
        var job = new CountingJob();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ISyncJob>(job);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddHostedService<PeriodicSyncWorker>();
        using var host = builder.Build();
        await host.StartAsync();

        Assert.True(await job.Ran.WaitAsync(TimeSpan.FromSeconds(5)));            // run 1, at start
        for (var run = 2; run <= 3; run++)
        {
            time.Advance(PeriodicSyncWorker.Interval);
            Assert.True(await job.Ran.WaitAsync(TimeSpan.FromSeconds(5)), $"run {run}");   // run 2 throws, run 3 still happens
        }

        await host.StopAsync();
        Assert.Equal(3, job.Runs);
    }

    [Fact]
    public async Task HealthCheck_ResolvesTheWorker_AndReportsAFault()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
        builder.Services.AddWorkerWithHealthCheck<FailingWorker>("failing-worker");
        using var host = builder.Build();
        await host.StartAsync();

        var worker = host.Services.GetRequiredService<FailingWorker>();
        Assert.Same(worker, host.Services.GetServices<IHostedService>().OfType<FailingWorker>().Single());
        await Assert.ThrowsAnyAsync<Exception>(() => worker.ExecuteTask!);

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.Equal(HealthStatus.Unhealthy, report.Entries["failing-worker"].Status);
        await host.StopAsync();
    }
}
