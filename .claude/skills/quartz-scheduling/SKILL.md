---
name: quartz-scheduling
description: Quartz.NET in-process scheduling on PostgreSQL — clustered persistent store with the schema applied by the pipeline, cron in a real time zone, misfires, no overlapping runs, a health check and admin endpoints. Tested in CI against PostgreSQL.
version: 2.0.0
---

# Quartz.NET Scheduling Patterns

Use Quartz for schedules inside a long-running service: cron with calendars and time zones, misfire
rules, and one run across several pods (clustering). For fire-and-forget work from requests see
`hangfire-patterns`; for a run-once process on a schedule see `cronjob-patterns`.

## Setup

Packages: `Quartz.Extensions.Hosting` and `Quartz.Serialization.SystemTextJson` (3.x).

<!-- sample: tests/SkillSamples.Tests/Scheduling/QuartzSetup.cs -->
```csharp
public static class QuartzSetup
{
    public static IServiceCollection AddScheduling(this IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q =>
        {
            q.SchedulerName = "orders-scheduler";   // the same on every instance of the service
            q.SchedulerId = "AUTO";                  // a different id per instance

            q.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);  // tables from Quartz's script, applied by the pipeline
                store.UseSystemTextJsonSerializer();
                store.UseClustering(cluster =>
                {
                    cluster.CheckinInterval = TimeSpan.FromSeconds(15);
                    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(30);
                });
            });
            q.UseDefaultThreadPool(pool => pool.MaxConcurrency = 10);

            // Cron runs in the scheduler's local time zone unless told otherwise, and pods run in UTC.
            var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
            q.AddJob<DailyReportJob>(job => job.WithIdentity(DailyReportJob.Key).StoreDurably());
            q.AddTrigger(trigger => trigger
                .ForJob(DailyReportJob.Key)
                .WithIdentity("daily-report-trigger")
                .WithCronSchedule("0 0 2 * * ?", cron => cron
                    .InTimeZone(riyadh)                          // 02:00 Riyadh = 23:00 UTC
                    .WithMisfireHandlingInstructionFireAndProceed()));   // missed while down → run once on start
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
            options.AwaitApplicationStarted = true;
        });
        return services;
    }
}
```

- **Jobs get constructor injection by default.** `UseMicrosoftDependencyInjectionJobFactory()` is
  obsolete since 3.7; remove it (with warnings as errors it breaks the build).
- **Clustering:** every pod uses the same `SchedulerName` and its own id (`AUTO`). The persistent store
  is what lets a trigger fire on one pod only; with the in-memory store each pod runs every job.
- **Registering jobs on every start is safe:** tested, a second start updates the stored job and
  trigger instead of failing on them.
- **Time zones:** a cron trigger runs in the scheduler's local time zone, and pods run in UTC. Tested:
  `0 0 2 * * ?` in `Asia/Riyadh` fires at 23:00 UTC. Use IANA ids (`Asia/Riyadh`); Windows ids need ICU
  on Linux.
- **Misfires:** a run missed while every pod was down is handled by the trigger's misfire rule.
  `FireAndProceed` runs it once at start-up; `DoNothing` skips it. Pick per job; the default for cron is
  `FireAndProceed` too, but say it so the next reader knows it was a choice.

### The schema: Quartz's script, applied by the pipeline

Quartz never creates its tables. Its script is `database/tables/tables_postgres.sql` in the Quartz.NET
repository (the tests use the one from v3.22.4).
- **Set `DropDb` to 0 before running it.** The script starts by **dropping every Quartz table** when
  `DropDb` is 1, which is its default. Applied to production as shipped, it wipes every job and trigger.
- Apply it once, through the pipeline, like any reviewed migration script.
- **Its columns are `text` and `bytea`** (tested: `qrtz_job_details.job_name text`,
  `qrtz_job_details.job_data bytea`). That breaks the bounded-columns and no-binary rules, and it's a
  third-party schema you can't change: keep it in its own schema or database, and record it as the
  narrow exception the column rules allow.

## Jobs

<!-- sample: tests/SkillSamples.Tests/Scheduling/DailyReportJob.cs -->
```csharp
public interface IDailyReportBuilder
{
    Task BuildAsync(DateTimeOffset scheduledFor, CancellationToken ct);
}

[DisallowConcurrentExecution]   // a second trigger waits until this run ends (per job key, across the cluster)
public sealed class DailyReportJob(IDailyReportBuilder reports, ILogger<DailyReportJob> logger) : IJob
{
    public static readonly JobKey Key = new("daily-report");

    public async Task Execute(IJobExecutionContext context)
    {
        // The time it was meant for, not the time it ran: a misfired run still reports the right day.
        var scheduledFor = context.ScheduledFireTimeUtc ?? context.FireTimeUtc;
        try
        {
            await reports.BuildAsync(scheduledFor, context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Daily report for {ScheduledFor} failed", scheduledFor);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
```

- `[DisallowConcurrentExecution]` applies per job key, across the cluster. Tested: three triggers of the
  same job ran one after another, never two at once.
- Use `ScheduledFireTimeUtc` for "which day is this run for"; `FireTimeUtc` is when it actually ran.
- Pass `context.CancellationToken` to every call: it's cancelled when the service stops.
- `JobExecutionException(ex, refireImmediately: false)` records the failure without a tight retry loop.
  For retries with backoff inside one run, wrap the call in a `polly-resilience` pipeline.
- `context.Result` is only seen by job listeners; it isn't stored anywhere.

## Cron expressions (Quartz has seconds)

```
Expression              Description
──────────────────────  ──────────────────────────
0 0/5 * * * ?           Every 5 minutes
0 0 * * * ?             Every hour (top of hour)
0 0 2 * * ?             Daily at 2:00 (in the trigger's time zone)
0 0 2 ? * MON-FRI       Weekdays at 2:00
0 0 2 ? * SUN-THU       Sunday to Thursday at 2:00
0 0 0 1 * ?             First day of month at midnight
0 0 0 1,15 * ?          1st and 15th of month
0 0/30 8-16 * * ?       Every 30 minutes from 8:00 to 16:30
```

Tested: `0 0/30 8-17 * * ?` doesn't stop at 17:00; its last run of the day is 17:30. The hour field is
the hours that runs *start* in.

## Health check

<!-- sample: tests/SkillSamples.Tests/Scheduling/QuartzHealthCheck.cs -->
```csharp
public sealed class QuartzHealthCheck(ISchedulerFactory schedulerFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        if (!scheduler.IsStarted || scheduler.InStandbyMode || scheduler.IsShutdown)
            return HealthCheckResult.Unhealthy("Scheduler is not running");

        var metadata = await scheduler.GetMetaData(ct);
        return HealthCheckResult.Healthy($"Running on {metadata.SchedulerInstanceId}, {metadata.NumberOfJobsExecuted} jobs run");
    }
}
```

Tested: healthy while running, unhealthy in standby.

## Admin endpoints

<!-- sample: tests/SkillSamples.Tests/Scheduling/JobAdminEndpoints.cs -->
```csharp
// Behind an admin policy; every action checks the job exists, so a typo is a 404, not a silent no-op.
public static class JobAdminEndpoints
{
    public static RouteGroupBuilder MapJobAdmin(this IEndpointRouteBuilder app, string policy)
    {
        var group = app.MapGroup("/admin/jobs").RequireAuthorization(policy);
        group.MapPost("/{name}/trigger", (string name, ISchedulerFactory f, CancellationToken ct) =>
            Act(f, name, (s, key) => s.TriggerJob(key, ct), ct));
        group.MapPost("/{name}/pause", (string name, ISchedulerFactory f, CancellationToken ct) =>
            Act(f, name, (s, key) => s.PauseJob(key, ct), ct));
        group.MapPost("/{name}/resume", (string name, ISchedulerFactory f, CancellationToken ct) =>
            Act(f, name, (s, key) => s.ResumeJob(key, ct), ct));
        return group;
    }

    private static async Task<Results<Accepted, NotFound>> Act(
        ISchedulerFactory factory, string name, Func<IScheduler, JobKey, Task> action, CancellationToken ct)
    {
        var scheduler = await factory.GetScheduler(ct);
        var key = new JobKey(name);
        if (!await scheduler.CheckExists(key, ct))
            return TypedResults.NotFound();

        await action(scheduler, key);
        return TypedResults.Accepted((string?)null);
    }
}
```

Tested: an unknown job is 404; pause and resume change the trigger's state.

## Testing

Test a job by calling `Execute` with a mocked `IJobExecutionContext`, or run the real scheduler against
real storage as the samples here do. Quartz keeps the first logger factory it sees in a static, so tests
that build several hosts in one process must give them a factory that outlives them
(`services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)`); otherwise the second host fails
with `ObjectDisposedException: LoggerFactory`.

## Rules
- Persistent store and clustering when more than one pod runs the service; same scheduler name, `AUTO` id.
- Quartz's schema from its script with `DropDb = 0`, applied by the pipeline; a recorded exception to the
  column rules.
- Cron triggers in an explicit time zone (IANA id); an explicit misfire rule.
- `[DisallowConcurrentExecution]` on jobs that must not overlap; `context.CancellationToken` everywhere.
- No `UseMicrosoftDependencyInjectionJobFactory()`: DI is the default.
