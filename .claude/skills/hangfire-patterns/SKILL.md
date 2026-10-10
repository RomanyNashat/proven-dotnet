---
name: hangfire-patterns
description: Hangfire for .NET on PostgreSQL or SQL Server — setup with the schema applied by the pipeline, filters on the interface (where Hangfire reads them), queues, retries, continuations, recurring jobs in a real time zone, a dashboard behind a policy. Tested in CI against PostgreSQL.
version: 2.0.0
---

# Hangfire Patterns

## When to use Hangfire vs Quartz vs a CronJob

| Use Hangfire | Use Quartz.NET | Use a Kubernetes CronJob |
|-------------|----------------|-----------------|
| Fire-and-forget from web requests | Sub-minute precision scheduling | Isolated execution, scale-to-zero |
| Need a visual dashboard | Need clustered in-process scheduling | Data migration jobs |
| Delayed jobs (send email in 30 min) | Complex trigger logic (calendars) | Heavy batch processing |
| Job continuations (A then B) | Job chaining with dependencies | Simple scheduled tasks |
| Persistent retry with visibility | High-throughput scheduling | VM/bare-metal cron replacement |

## Setup

Packages: `Hangfire.AspNetCore` 1.8.x and `Hangfire.PostgreSql` 1.20.x (or `Hangfire.SqlServer`).

<!-- sample: tests/SkillSamples.Tests/Jobs/HangfireSetup.cs -->
```csharp
public static class HangfireSetup
{
    // Every service that enqueues needs the storage; only the ones that run jobs add the servers.
    public static IServiceCollection AddJobStorage(this IServiceCollection services, string connectionString) =>
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = false,          // the schema is applied by the pipeline, not at boot
                    QueuePollInterval = TimeSpan.FromSeconds(1) // the default is 15 s: jobs wait that long to start
                }));

    public static IServiceCollection AddJobServers(this IServiceCollection services)
    {
        // The order of a server's queue list is not a priority (on PostgreSQL it's first come, first
        // served), so urgent work gets its own server: a backlog of other jobs can't hold it up.
        services.AddHangfireServer(options => options.Queues = ["critical"]);
        services.AddHangfireServer(options =>
        {
            options.Queues = ["default", "low"];
            options.SchedulePollingInterval = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
```

- **Poll interval:** Hangfire.PostgreSql checks the queues every **15 seconds** by default, so a
  fire-and-forget job can wait that long to start. Lower `QueuePollInterval` when that matters.
- **A server's queue list is not a priority order.** Tested on PostgreSQL: one server listening to
  `critical, default, low` with one worker ran a `low` job before a `critical` one enqueued after it.
  For work that mustn't wait behind a backlog, give its queue a server of its own, as above.
- **Storage and servers are separate.** An API that only enqueues adds the storage; the services that run
  jobs add the servers too:

```csharp
builder.Services.AddJobStorage(connectionString).AddJobServers();
```

### The schema: applied by the pipeline, and an exception to the column rules

- `PrepareSchemaIfNecessary = false`. Like migrations, the app never changes the schema at boot
  (`rules/efcore-rules.md`). Apply it as a deploy step: a small one-off that calls
  `PostgreSqlObjectsInstaller.Install(connection, "hangfire")` (SQL Server:
  `SqlServerObjectsInstaller.Install(connection, "HangFire", false)`), run by the pipeline before the
  service starts, the same way as reviewed migration scripts. The tests apply it exactly that way.
- **Hangfire's tables use unbounded columns** (tested: `hangfire.job.invocationdata` is `text` on
  PostgreSQL; SQL Server uses `nvarchar(max)`). That breaks the bounded-columns rule, and you can't change
  a third-party schema. Keep it in its own schema (or database), and record it as the narrow exception the
  column rules allow, with the DBA if there is one.

## Jobs

<!-- sample: tests/SkillSamples.Tests/Jobs/ReceiptJobs.cs -->
```csharp
// Hangfire stores the job as "call SendAsync on IReceiptSender". Filters such as [AutomaticRetry] and
// [Queue] are read from that type, so they go on the interface method, not on the class.
public interface IReceiptSender
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 600])]
    Task SendAsync(int orderId, CancellationToken ct);
}

public sealed class OrderJobs(IBackgroundJobClient jobs)
{
    // CancellationToken.None is a placeholder: Hangfire passes its own token, cancelled on shutdown.
    public string QueueReceipt(int orderId) =>
        jobs.Enqueue<IReceiptSender>("default", sender => sender.SendAsync(orderId, CancellationToken.None));
}
```

**Filters go on the interface.** Tested: a job enqueued as `Enqueue<IExport>(...)` with
`[AutomaticRetry(Attempts = 0)]` on the implementing class is still retried (state `Scheduled`); the same
attribute on the interface method makes it fail at once (state `Failed`). The same holds for `[Queue]`.
Prefer the queue argument on `Enqueue`, as above.

- **Arguments are stored**, so pass small, stable values: an `int` id, not an entity. The job loads what
  it needs when it runs.
- **`CancellationToken.None` is a placeholder.** Hangfire passes its own token at run time (tested: it's
  cancellable), cancelled when the server shuts down. Pass it to every I/O call.
- **Enqueuing after `SaveChanges` is a dual write.** If the process dies between the commit and the
  enqueue, the job is lost. Where that matters, enqueue from the outbox relay (`outbox`), not from the
  request.

### Jobs run at least once: make them idempotent

A retried or restarted job runs again. "Check a flag, send, then set the flag" still sends twice if the
process dies between the send and the save. Make the side effect itself idempotent (an idempotency key
the provider honours, a unique constraint on what the job writes), or claim the work first in one
statement and accept that a crash after the claim needs a manual retry.

```csharp
public sealed class ReceiptSender(IOrderRepository orders, IEmailSender email) : IReceiptSender
{
    public async Task SendAsync(int orderId, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct);
        if (order is null)
            return;                                   // nothing to retry

        // The provider deduplicates on this key, so a second run sends nothing.
        await email.SendAsync(order.ReceiptEmail(), idempotencyKey: $"receipt-{orderId}", ct);
    }
}
```

## Retries

- Hangfire's global default is 10 attempts with growing delays. Set your own on the interface method
  (above), or globally with `config.UseFilter(new AutomaticRetryAttribute { Attempts = 3 })`.
- `[AutomaticRetry(Attempts = 0)]` for jobs that must not run twice; the job goes to `Failed` and waits
  for a person (dashboard: Retry).

## Continuations

```csharp
var validate = jobs.Enqueue<IOrderSteps>(s => s.ValidateAsync(orderId, CancellationToken.None));
jobs.ContinueJobWith<IOrderSteps>(validate, s => s.ChargeAsync(orderId, CancellationToken.None));
```

Tested: the continuation runs after its parent succeeds. If the parent fails, the continuation stays
`Awaiting` and never runs on its own: retrying the parent from the dashboard releases it.

## Recurring jobs

<!-- sample: tests/SkillSamples.Tests/Jobs/RecurringJobs.cs -->
```csharp
public interface IDailyReport
{
    Task GenerateAsync(CancellationToken ct);
}

public static class RecurringJobs
{
    public static void Register(IRecurringJobManager recurring)
    {
        // The IANA id works on Linux and Windows. "Arab Standard Time" (the Windows id) needs ICU on Linux,
        // which Alpine images don't have unless they add it.
        var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

        recurring.AddOrUpdate<IDailyReport>(
            "daily-report", "low",
            report => report.GenerateAsync(CancellationToken.None),
            Cron.Daily(2),                                   // 02:00 in Riyadh, 23:00 UTC the day before
            new RecurringJobOptions { TimeZone = riyadh });
    }
}
```

Tested: the next run is stored as 23:00 UTC, and the queue travels with the job (`RecurringJobDto.Job.Queue`;
the older `RecurringJobDto.Queue` still reads `default`). Register through `IRecurringJobManager` (injected, so it can
be tested) at startup; `AddOrUpdate` with the same id replaces the job, so registering on every start is
safe. Remove one that's gone with `RemoveIfExists`.

## Dashboard (secured)

<!-- sample: tests/SkillSamples.Tests/Jobs/HangfireDashboard.cs -->
```csharp
// The dashboard can retry, delete and trigger jobs: put it behind the same policy system as the API.
public sealed class DashboardPolicyFilter(string policy) : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, policy)).Succeeded;
    }
}

public static class HangfireDashboard
{
    public const string Policy = "JobsAdmin";

    public static IEndpointConventionBuilder MapJobsDashboard(this IEndpointRouteBuilder app) =>
        app.MapHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [],                                   // drop the default local-requests-only filter
            AsyncAuthorization = [new DashboardPolicyFilter(Policy)],
            DisplayStorageConnectionString = false
        });
}
```

```csharp
// Program.cs
builder.Services.AddAuthorization(o => o.AddPolicy(HangfireDashboard.Policy, p => p.RequireRole("jobs-admin")));
app.UseAuthentication();
app.UseAuthorization();
app.MapJobsDashboard();
```

Tested: anonymous gets 401, a signed-in user without the role 403, the role 200. Without
`Authorization = []`, Hangfire's default filter also allows local requests only, which in a pod behind
an ingress means nobody.

## Testing jobs

Test the job class directly, with its dependencies mocked; no Hangfire needed. Test the wiring (filters,
queues, recurring schedules) against real storage, as the samples here do.

```csharp
[Fact]
public async Task SendAsync_OrderMissing_SendsNothing()
{
    var orders = new Mock<IOrderRepository>();
    var email = new Mock<IEmailSender>();
    var sender = new ReceiptSender(orders.Object, email.Object);

    await sender.SendAsync(42, CancellationToken.None);

    email.VerifyNoOtherCalls();
}
```

## Rules
- Schema applied by the pipeline, never `PrepareSchemaIfNecessary = true`; Hangfire's own tables are a
  recorded exception to the column rules.
- Filters (`[AutomaticRetry]`, `[Queue]`) on the interface method you enqueue through.
- Queue order is not priority: urgent queues get their own server.
- Small, stable arguments (`int` ids); `CancellationToken.None` in the expression, used in the job.
- Jobs run at least once: idempotent side effects.
- Time zones by IANA id (`Asia/Riyadh`), not Windows ids.
- Dashboard behind an authorization policy, with the local-only default removed.
