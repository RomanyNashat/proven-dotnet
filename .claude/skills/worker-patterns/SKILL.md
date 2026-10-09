---
name: worker-patterns
description: Background workers for .NET: BackgroundService vs IHostedService, Channel<T> queues that drain on shutdown, PeriodicTimer with TimeProvider, worker health checks, graceful shutdown. Code tested in CI.
version: 1.1.0
---

# Worker & Background Service Patterns

The code marked as a sample is compiled and tested in CI (`tests/SkillSamples.Tests/Workers`): the queue
drains on shutdown (and a test shows the common version losing messages), the drain stops at the
shutdown timeout, the periodic worker survives a failed run, and the health check resolves its worker.

## IHostedService vs BackgroundService

| Use `IHostedService` | Use `BackgroundService` |
|---------------------|------------------------|
| Short startup/shutdown tasks | Long-running loops |
| Warm a cache on startup | Queue consumers (Kafka) |
| One-time initialization | Periodic work (`PeriodicTimer`) |

`BackgroundService` is an `IHostedService` that runs `ExecuteAsync` for the life of the host.

## The host

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));   // SQL Server: UseSqlServer
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<OrderProcessingWorker>();

builder.Services.Configure<HostOptions>(o =>
{
    o.ShutdownTimeout = TimeSpan.FromSeconds(30);   // time to drain on SIGTERM
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;   // the default; keep it
});

await builder.Build().RunAsync();
```

The pod's `terminationGracePeriodSeconds` must be longer than `ShutdownTimeout` (for example 45 against
30), or Kubernetes kills the process mid-drain (`kubernetes-dotnet`).

## A polling worker

```csharp
public sealed class OrderProcessingWorker(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<OrderProcessingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextBatchAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), time, stoppingToken);   // nothing to do: wait
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Batch failed; retrying in 5 s");
                await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken);
            }
        }
    }

    private async Task<int> ProcessNextBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();   // DbContext and repositories are scoped
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var orders = await repository.GetPendingAsync(batchSize: 10, ct);
        foreach (var order in orders)
        {
            order.Process();
        }

        await repository.SaveChangesAsync(ct);
        return orders.Count;
    }
}
```

On .NET 8, everything in `ExecuteAsync` before its first `await` runs during startup and blocks the
host; start with `await Task.Yield()` there. .NET 10 runs the whole of `ExecuteAsync` in the background.

With more than one pod, every pod runs this loop. Claim rows so two pods don't process the same order,
or run the job in one pod only (a lock, as in the `outbox` relay). Claiming a batch:

<!-- sample: tests/SkillSamples.Tests/Workers/WorkClaims.cs -->
```csharp
// Each pod claims a batch of pending rows; a row another pod holds is skipped, not waited on.
// A service has one engine and keeps one statement.
public static class WorkClaims
{
    public const string PostgreSql = """
        UPDATE work_items SET claimed_by = @pod
        WHERE id IN (
            SELECT id FROM work_items
            WHERE status = 'Pending' AND claimed_by IS NULL
            ORDER BY id
            LIMIT @n
            FOR UPDATE SKIP LOCKED)
        RETURNING id
        """;

    public const string SqlServer = """
        UPDATE TOP (@n) work_items WITH (UPDLOCK, READPAST, ROWLOCK)
        SET claimed_by = @pod
        OUTPUT inserted.id
        WHERE status = 'Pending' AND claimed_by IS NULL
        """;
}
```

Tested on both engines: while one pod's transaction still holds its ten rows, a second pod claims the
other ten at once, without waiting. On SQL Server, `TOP` in an `UPDATE` takes no `ORDER BY`; when the
order matters, select the `TOP … ORDER BY` in a CTE and update the CTE. Claiming skips locked rows, so
it gives up order between batches: events of one aggregate that must leave in order need one relay
(`outbox`).

## Periodic work: PeriodicTimer with TimeProvider

<!-- sample: tests/SkillSamples.Tests/Workers/PeriodicSyncWorker.cs -->
```csharp
public interface ISyncJob
{
    Task RunAsync(CancellationToken ct);
}

// Runs once at start, then every interval. One failed run is logged and the next tick runs anyway.
// The TimeProvider makes the interval testable without waiting for it.
public sealed class PeriodicSyncWorker(ISyncJob job, TimeProvider time, ILogger<PeriodicSyncWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await job.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sync failed; next run in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

## An in-process queue that drains on shutdown

<!-- sample: tests/SkillSamples.Tests/Workers/NotificationQueue.cs -->
```csharp
public sealed record NotificationMessage(int Id, string Text);

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken ct);
}

// In-process only: anything still queued is lost if the pod is killed. A message that must not be lost
// goes through the outbox and Kafka instead.
public sealed class NotificationQueue
{
    private readonly Channel<NotificationMessage> _channel = Channel.CreateBounded<NotificationMessage>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });   // backpressure when full

    public ChannelReader<NotificationMessage> Reader => _channel.Reader;

    // Throws ChannelClosedException once shutdown has started; callers treat that as "try later".
    public ValueTask EnqueueAsync(NotificationMessage message, CancellationToken ct) => _channel.Writer.WriteAsync(message, ct);

    public void Complete() => _channel.Writer.TryComplete();
}

public sealed class NotificationDispatchWorker(
    NotificationQueue queue, IServiceScopeFactory scopeFactory, ILogger<NotificationDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ends by itself when the queue is completed and empty (see StopAsync).
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<INotificationSender>().SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to dispatch notification {Id}", message.Id);
            }
        }
    }

    // Drain before cancelling. BackgroundService.StopAsync cancels stoppingToken straight away, so calling
    // it first drops whatever is still queued. Instead: refuse new items, wait for the queue to empty (up
    // to the host's shutdown timeout, which is what cancellationToken carries), then cancel the rest.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        if (ExecuteTask is { } running)
        {
            await Task.WhenAny(running, Task.Delay(Timeout.Infinite, cancellationToken));
        }

        await base.StopAsync(cancellationToken);
    }
}
```

**The common version loses messages.** Overriding `StopAsync` as "complete the writer, then
`base.StopAsync()`" looks like a drain, but `base.StopAsync` cancels `stoppingToken` at once, and the
read loop stops with messages still queued. A test in CI shows it losing them; the version above sends
all of them, and still stops when the host's shutdown timeout runs out.

Several consumers on one queue: start N copies of the read loop with `Task.WhenAll`. Message order is
then not kept.

## Health checks for workers

<!-- sample: tests/SkillSamples.Tests/Workers/WorkerHealth.cs -->
```csharp
public sealed class WorkerHealthCheck<TWorker>(TWorker worker) : IHealthCheck where TWorker : BackgroundService
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct) =>
        Task.FromResult(worker.ExecuteTask switch
        {
            null => HealthCheckResult.Degraded("Worker has not started"),
            { IsFaulted: true } t => HealthCheckResult.Unhealthy("Worker has faulted", t.Exception),
            { IsCompleted: true } => HealthCheckResult.Unhealthy("Worker has stopped"),
            _ => HealthCheckResult.Healthy("Worker is running"),
        });
}

public static class WorkerRegistration
{
    // AddHostedService<T>() alone registers the worker only as IHostedService, so nothing can inject it
    // as T, and a health check that asks for T fails to resolve. Register the instance once as itself,
    // and hand that same instance to the host.
    public static IServiceCollection AddWorkerWithHealthCheck<TWorker>(this IServiceCollection services, string name)
        where TWorker : BackgroundService
    {
        services.AddSingleton<TWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<TWorker>());
        services.AddHealthChecks().AddCheck<WorkerHealthCheck<TWorker>>(name, tags: ["ready"]);
        return services;
    }
}
```

```csharp
builder.Services.AddWorkerWithHealthCheck<OrderProcessingWorker>("order-processing-worker");
// A worker that serves probes is an ASP.NET Core app (WebApplication), so it has endpoints:
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
```

## Images

There is no Dockerfile in the repo: your pipeline builds the image. A worker that serves health
checks runs on the ASP.NET Core runtime, not the plain .NET runtime.

## Anti-patterns
- `Thread.Sleep()`. Use `await Task.Delay(..., timeProvider, ct)` or `PeriodicTimer`.
- `Task.Run()` around synchronous work inside `ExecuteAsync`.
- Swallowing `OperationCanceledException` in a loop: catch with `when (ex is not OperationCanceledException)`.
- Injecting scoped services into a worker (it's a singleton). Use `IServiceScopeFactory`.
- `base.StopAsync()` before the queue is empty.
- Injecting a worker registered only with `AddHostedService<T>()`: it can't be resolved as `T`.
- An in-process queue for messages that must not be lost. Use the outbox and Kafka.
