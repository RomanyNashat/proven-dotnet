---
name: quartz-scheduling
description: Quartz.NET in-process scheduling: jobs, triggers, cron schedules, persistence, clustering.
version: 1.0.0
---

# Quartz.NET Scheduling Patterns

## Setup with DI

```csharp
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    // Clustered mode — multiple instances coordinate via database
    q.UsePersistentStore(store =>
    {
        store.UsePostgres(builder.Configuration.GetConnectionString("QuartzDb")!);
        store.UseNewtonsoftJsonSerializer();
        store.UseClustering(cluster =>
        {
            cluster.CheckinInterval = TimeSpan.FromSeconds(15);
            cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
        });
    });

    q.UseDefaultThreadPool(pool => pool.MaxConcurrency = 10);

    // Register jobs
    q.AddJob<LeaderboardSyncJob>(opts => opts
        .WithIdentity("leaderboard-sync")
        .StoreDurably());

    q.AddTrigger(opts => opts
        .ForJob("leaderboard-sync")
        .WithIdentity("leaderboard-sync-trigger")
        .WithCronSchedule("0 */5 * * * ?")  // every 5 minutes
        .WithDescription("Sync leaderboard scores"));

    q.AddJob<DailyReportJob>(opts => opts
        .WithIdentity("daily-report")
        .StoreDurably());

    q.AddTrigger(opts => opts
        .ForJob("daily-report")
        .WithIdentity("daily-report-trigger")
        .WithCronSchedule("0 0 2 * * ?")  // 2 AM daily
        .StartAt(DateBuilder.TomorrowAt(2, 0, 0))
        .WithDescription("Generate daily analytics report"));

    q.AddJob<ExpiredChallengeCleanupJob>(opts => opts
        .WithIdentity("expired-challenge-cleanup")
        .StoreDurably());

    q.AddTrigger(opts => opts
        .ForJob("expired-challenge-cleanup")
        .WithIdentity("expired-challenge-trigger")
        .WithCronSchedule("0 0 */1 * * ?")  // every hour
        .WithDescription("Clean up expired challenges"));
});

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
    options.AwaitApplicationStarted = true;
});
```

## Job Implementation

```csharp
[DisallowConcurrentExecution]  // prevents overlap if previous run is still going
public sealed class LeaderboardSyncJob(
    ILeaderboardService leaderboardService,
    ILogger<LeaderboardSyncJob> logger,
    TimeProvider timeProvider) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var jobName = context.JobDetail.Key.Name;
        var fireTime = context.FireTimeUtc;
        logger.LogInformation("Job {JobName} started at {FireTime}", jobName, fireTime);

        try
        {
            var sw = Stopwatch.StartNew();

            var syncResult = await leaderboardService.SyncAllAsync(
                context.CancellationToken);

            sw.Stop();
            logger.LogInformation(
                "Job {JobName} completed in {ElapsedMs}ms. Synced {Count} entries",
                jobName, sw.ElapsedMilliseconds, syncResult.EntriesSynced);

            // Store result in JobDataMap for monitoring
            context.Result = $"Synced {syncResult.EntriesSynced} entries";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Job {JobName} failed", jobName);

            // Quartz retry: throw JobExecutionException with refireImmediately
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
```

## Job with Polly Retry

```csharp
public sealed class ResilientApiSyncJob(
    IExternalApiClient apiClient,
    ILogger<ResilientApiSyncJob> logger,
    [FromKeyedServices("api-sync")] ResiliencePipeline pipeline) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await pipeline.ExecuteAsync(async ct =>
        {
            var data = await apiClient.FetchLatestAsync(ct);
            logger.LogInformation("Fetched {Count} records from external API", data.Count);
        }, context.CancellationToken);
    }
}
```

## Common Cron Expressions

```
Expression              Description
──────────────────────  ──────────────────────────
0 0/5 * * * ?           Every 5 minutes
0 0 * * * ?             Every hour (top of hour)
0 0 2 * * ?             Daily at 2:00 AM
0 0 2 ? * MON-FRI       Weekdays at 2:00 AM
0 0 0 1 * ?             First day of month at midnight
0 0 0 1,15 * ?          1st and 15th of month
0 0 0 ? * SUN           Every Sunday at midnight
0 */30 8-17 * * ?       Every 30 min during business hours (8-17)
```

## Monitoring Scheduled Jobs

```csharp
// Health check for Quartz scheduler
builder.Services.AddHealthChecks()
    .AddCheck<QuartzHealthCheck>("quartz-scheduler", tags: ["ready"]);

public sealed class QuartzHealthCheck(ISchedulerFactory schedulerFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);

        if (!scheduler.IsStarted || scheduler.InStandbyMode)
            return HealthCheckResult.Unhealthy("Scheduler is not running");

        var metadata = await scheduler.GetMetaData(ct);
        return HealthCheckResult.Healthy(
            $"Running. Jobs executed: {metadata.NumberOfJobsExecuted}");
    }
}
```

## Job Management Endpoints (admin)

```csharp
app.MapGroup("/api/admin/jobs")
    .RequireAuthorization("AdminOnly")
    .MapJobManagementEndpoints();

public static class JobManagementEndpoints
{
    public static RouteGroupBuilder MapJobManagementEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{jobName}/trigger", async (
            string jobName, ISchedulerFactory factory, CancellationToken ct) =>
        {
            var scheduler = await factory.GetScheduler(ct);
            var jobKey = new JobKey(jobName);

            if (!await scheduler.CheckExists(jobKey, ct))
                return TypedResults.NotFound();

            await scheduler.TriggerJob(jobKey, ct);
            return TypedResults.Ok($"Job {jobName} triggered");
        });

        group.MapPost("/{jobName}/pause", async (
            string jobName, ISchedulerFactory factory, CancellationToken ct) =>
        {
            var scheduler = await factory.GetScheduler(ct);
            await scheduler.PauseJob(new JobKey(jobName), ct);
            return TypedResults.Ok($"Job {jobName} paused");
        });

        group.MapPost("/{jobName}/resume", async (
            string jobName, ISchedulerFactory factory, CancellationToken ct) =>
        {
            var scheduler = await factory.GetScheduler(ct);
            await scheduler.ResumeJob(new JobKey(jobName), ct);
            return TypedResults.Ok($"Job {jobName} resumed");
        });

        return group;
    }
}
```
