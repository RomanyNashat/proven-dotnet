---
name: cronjob-patterns
description: Cron jobs for .NET (not Quartz in-process): run-once console apps via K8s CronJob/crontab, manifests, retries, data-migration jobs.
version: 1.0.0
---

# Cron Job Patterns (Run-Once Console Apps)

## When to Use What

| Pattern | Use When |
|---------|----------|
| **K8s CronJob + Console App** | Scheduled tasks in Kubernetes, need isolation, scale-to-zero between runs |
| **Quartz.NET** | In-process scheduling inside a long-running service, need clustering, need sub-minute precision |
| **Hangfire** | Need dashboard, fire-and-forget from web requests, persistent job queue |
| **crontab / Task Scheduler** | Simple VM-based deployments, no Kubernetes |

This skill covers the **console app + external scheduler** pattern. For Quartz.NET, see `quartz-scheduling`. For Hangfire, see `hangfire-patterns`.

## Console App Job (run once, exit)

The job runs, stops the host, and **Main returns its exit code**. That code is what Kubernetes reads:
non-zero and `backoffLimit` retries the run; zero and the run is recorded as a success.

### Program.cs

```csharp
return await DailyReportHost.RunAsync(args, builder =>
{
    builder.Services.AddDbContext<ReportsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
    builder.Services.AddScoped<IReportService, ReportService>();
});
```

<!-- sample: tests/SkillSamples.Tests/CronJobs/DailyReportHost.cs -->
```csharp
public static class DailyReportHost
{
    /// <summary>Program.cs is <c>return await DailyReportHost.RunAsync(args, ...)</c>: the exit code Kubernetes reads.</summary>
    public static async Task<int> RunAsync(string[] args, Action<HostApplicationBuilder> configure)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton<JobOutcome>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<DailyReportJob>();
        configure(builder);   // the report service and its database

        using var host = builder.Build();
        var outcome = host.Services.GetRequiredService<JobOutcome>();   // before RunAsync, which disposes the host
        await host.RunAsync();
        return outcome.ExitCode;
    }
}
```

`RunAsync` disposes the host when it returns, so read anything you need from `host.Services` before it
(tested: reading the outcome afterwards throws `ObjectDisposedException`).

### Job Implementation

<!-- sample: tests/SkillSamples.Tests/CronJobs/DailyReportJob.cs -->
```csharp
public sealed class DailyReportJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    JobOutcome outcome,
    TimeProvider time,
    ILogger<DailyReportJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var day = ReportDay.Yesterday(time, RiyadhTime.Zone);
            await using var scope = scopeFactory.CreateAsyncScope();
            var records = await scope.ServiceProvider.GetRequiredService<IReportService>()
                .GenerateDailyReportAsync(day, stoppingToken);
            logger.LogInformation("Daily report for {Day}: {Count} records", day, records);
        }
        catch (Exception ex)
        {
            // Includes a SIGTERM mid-run: the report isn't done, so the run failed and Kubernetes retries it.
            logger.LogCritical(ex, "Daily report failed");
            outcome.Failed();
        }
        finally
        {
            lifetime.StopApplication();   // run once: the host stops, Main returns the exit code
        }
    }
}
```

<!-- sample: tests/SkillSamples.Tests/CronJobs/JobOutcome.cs -->
```csharp
/// <summary>What a run-once job reports back to Main, which returns it as the process exit code.</summary>
public sealed class JobOutcome
{
    public int ExitCode { get; private set; }

    public void Failed() => ExitCode = 1;
}
```

### "Yesterday" is in the business's time zone

The CronJob below runs at 02:00 Riyadh (`timeZone: Asia/Riyadh`), which is **23:00 UTC the evening
before**. `DateTime.UtcNow.AddDays(-1)` then gives the day before yesterday: the report covers the wrong
day, every day, and nothing fails. Tested: at 23:00 UTC on 15 October the UTC way gives the 14th; this
gives the 15th, also on an image without tzdata (`RiyadhTime` falls back to a fixed UTC+3).

<!-- sample: tests/SkillSamples.Tests/CronJobs/ReportDay.cs -->
```csharp
public static class ReportDay
{
    /// <summary>
    /// The day before today in the business's time zone. A job scheduled for 02:00 Riyadh runs at 23:00 UTC
    /// the evening before, so "yesterday" worked out in UTC is the day before yesterday.
    /// </summary>
    public static DateOnly Yesterday(TimeProvider time, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime).AddDays(-1);
}
```

Tested (`CronJobs/CronJobTests.cs`): the report service fails → exit code 1, so Kubernetes retries; it
succeeds → exit code 0 and the report is for Riyadh's yesterday.

### .csproj
<!-- sample: tests/Infra/docker/src/DailyReport.Job/DailyReport.Job.csproj -->
```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <!-- The Worker SDK's implicit usings (Microsoft.Extensions.Hosting and friends) need the package. -->
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
</Project>
```

## Kubernetes CronJob Manifest

### Basic CronJob
<!-- sample: tests/Infra/k8s/cronjob.yaml -->
```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: daily-report
  namespace: order-service
spec:
  schedule: "0 2 * * *"  # 2:00 AM daily (UTC)
  timeZone: "Asia/Riyadh"  # K8s 1.27+: native timezone support
  concurrencyPolicy: Forbid  # don't run if previous is still running
  successfulJobsHistoryLimit: 3
  failedJobsHistoryLimit: 5
  startingDeadlineSeconds: 600  # skip if delayed more than 10 minutes
  jobTemplate:
    spec:
      backoffLimit: 3  # retry up to 3 times on failure
      activeDeadlineSeconds: 3600  # kill if running longer than 1 hour
      ttlSecondsAfterFinished: 86400  # clean up pod after 24 hours
      template:
        metadata:
          labels:
            app: daily-report
            type: cronjob
        spec:
          serviceAccountName: order-service-sa  # same SA for vault access
          restartPolicy: OnFailure  # MUST be OnFailure or Never for CronJobs
          containers:
            - name: daily-report
              image: registry.example.com/daily-report:1.0.0
              env:
                - name: DOTNET_ENVIRONMENT  # a console host doesn't read ASPNETCORE_ENVIRONMENT
                  value: "Production"
                - name: DOTNET_EnableDiagnostics_Debugger
                  value: "0"
                - name: DOTNET_EnableDiagnostics_Profiler
                  value: "0"
              envFrom:
                - configMapRef:
                    name: order-service-config
                - secretRef:
                    name: order-service-secrets
              resources:
                requests:
                  cpu: "250m"
                  memory: "256Mi"
                limits:
                  cpu: "1000m"
                  memory: "512Mi"
```

`DOTNET_ENVIRONMENT`, not `ASPNETCORE_ENVIRONMENT`: a console host built with `Host.CreateApplicationBuilder`
reads only the first (tested: with `ASPNETCORE_ENVIRONMENT=Staging` it runs as Production and never loads
`appsettings.Staging.json`). Validated in CI with kubeconform against Kubernetes 1.30.

### Common Cron Schedules
```yaml
"0 2 * * *"          # Daily at 2:00 AM
"0 */6 * * *"        # Every 6 hours
"0 0 * * 0"          # Weekly (Sunday midnight)
"0 0 1 * *"          # Monthly (1st day midnight)
"*/5 * * * *"        # Every 5 minutes
"0 8-17 * * 1-5"     # Hourly during business hours (Mon-Fri 8AM-5PM)
"0 2 * * 1-5"        # Weekdays at 2:00 AM
```

### ConcurrencyPolicy Options
```yaml
concurrencyPolicy: Forbid   # skip new run if previous still running (SAFEST)
concurrencyPolicy: Replace  # kill previous, start new
concurrencyPolicy: Allow    # allow overlapping runs (DANGEROUS for most jobs)
```

## Data Migration Job Pattern

Idempotent (a second run finds its record and stops), in batches, and **sure to finish**. Page by id,
never by "rows not migrated yet": a legacy value nobody mapped stays unmigrated, so a loop that reads
"rows still Unknown" until none are left gets the same rows back on every pass and runs until
`activeDeadlineSeconds` kills it. Tested: three orders, one with a status nobody mapped; the job
finishes, migrates two, and records one left Unknown.

<!-- sample: tests/SkillSamples.Tests/CronJobs/OrderStatusMigrationJob.cs -->
```csharp
/// <summary>A data migration as a run-once job: idempotent, in batches, and sure to finish.</summary>
public sealed class OrderStatusMigrationJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    JobOutcome outcome,
    TimeProvider time,
    ILogger<OrderStatusMigrationJob> logger) : BackgroundService
{
    public const string Name = "2026-10-order-status";
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CronDbContext>();
            if (await db.DataMigrations.AnyAsync(m => m.Name == Name, stoppingToken))
            {
                logger.LogInformation("{Migration} already ran; nothing to do", Name);
                return;
            }

            int lastId = 0, migrated = 0, unmapped = 0;
            while (true)
            {
                // Page by id. Paging by "rows still Unknown" never ends: a legacy value nobody mapped stays
                // Unknown, so the same rows come back on every pass.
                var batch = await db.Orders
                    .Where(o => o.Id > lastId && o.LegacyStatus != null && o.Status == OrderStatus.Unknown)
                    .OrderBy(o => o.Id)
                    .Take(BatchSize)
                    .ToListAsync(stoppingToken);
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var order in batch)
                {
                    order.Status = Map(order.LegacyStatus!);
                    if (order.Status == OrderStatus.Unknown)
                    {
                        unmapped++;
                    }
                    else
                    {
                        migrated++;
                    }
                }

                lastId = batch[^1].Id;
                await db.SaveChangesAsync(stoppingToken);
                db.ChangeTracker.Clear();
            }

            db.DataMigrations.Add(new DataMigrationRun
            {
                Name = Name, RecordsMigrated = migrated, RecordsUnmapped = unmapped, CompletedAt = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(stoppingToken);
            logger.LogInformation("{Migration}: {Migrated} migrated, {Unmapped} left Unknown", Name, migrated, unmapped);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "{Migration} failed", Name);
            outcome.Failed();
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    public static OrderStatus Map(string legacy) => legacy switch
    {
        "NEW" => OrderStatus.Placed,
        "SHIPPED" => OrderStatus.Shipped,
        "DONE" => OrderStatus.Delivered,
        _ => OrderStatus.Unknown,
    };
}
```

## Cleanup / Retention Job

Delete in batches. One `DELETE` of a year of rows holds its locks and fills the log for the whole run.
Tested: 12,000 expired rows go in three batches; the recent one stays.

<!-- sample: tests/SkillSamples.Tests/CronJobs/AuditRetentionJob.cs -->
```csharp
/// <summary>Deletes what's past its retention, in batches so one run never holds a long lock.</summary>
public sealed class AuditRetentionJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    JobOutcome outcome,
    TimeProvider time,
    ILogger<AuditRetentionJob> logger) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);
    private const int BatchSize = 5_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CronDbContext>();
            var cutoff = time.GetUtcNow() - Retention;

            int deleted, total = 0;
            do
            {
                var ids = db.AuditLog.Where(a => a.CreatedAt < cutoff).OrderBy(a => a.Id).Select(a => a.Id).Take(BatchSize);
                deleted = await db.AuditLog.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(stoppingToken);
                total += deleted;
            }
            while (deleted == BatchSize);

            logger.LogInformation("Retention: deleted {Count} audit entries older than {Cutoff}", total, cutoff);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Retention failed");
            outcome.Failed();
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
```


## Dockerfile for Console App Jobs

<!-- sample: tests/Infra/docker/src/DailyReport.Job/Dockerfile -->
```dockerfile
# Use runtime image (not aspnet — no web server needed)
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/DailyReport.Job/DailyReport.Job.csproj", "src/DailyReport.Job/"]
COPY ["Directory.Build.props", "Directory.Packages.props", "NuGet.Config", "./"]
RUN dotnet restore "src/DailyReport.Job/DailyReport.Job.csproj"
COPY . .
RUN dotnet publish "src/DailyReport.Job/DailyReport.Job.csproj" \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DailyReport.Job.dll"]
```

Tested in CI: it builds, a successful run exits 0 and a failed one exits 1 from the container, which is
what the CronJob's `backoffLimit` reads.

**Key difference from API/Worker Dockerfiles**: use `runtime` image (not `aspnet`) — no Kestrel, no HTTP stack, smaller image.

## crontab / Windows Task Scheduler (VM deployment)

### Linux crontab
```bash
# Edit crontab
crontab -e

# Run daily report at 2 AM
0 2 * * * cd /opt/jobs/daily-report && dotnet DailyReport.Job.dll >> /var/log/daily-report.log 2>&1

# Run cleanup every Sunday at 3 AM
0 3 * * 0 cd /opt/jobs/retention && dotnet RetentionJob.dll >> /var/log/retention.log 2>&1
```

### Windows Task Scheduler (PowerShell)
```powershell
$action = New-ScheduledTaskAction -Execute "dotnet" `
    -Argument "C:\Jobs\DailyReport\DailyReport.Job.dll" `
    -WorkingDirectory "C:\Jobs\DailyReport"

$trigger = New-ScheduledTaskTrigger -Daily -At 2:00AM

Register-ScheduledTask -TaskName "DailyReport" `
    -Action $action -Trigger $trigger `
    -Description "Generate daily analytics report" `
    -User "SYSTEM"
```

## Testing Console App Jobs

Run the job in a real host against a real database (`testing-integration`), and assert on what it
changed and on its exit code. `CronJobs/CronJobTests.cs` does both: the host is built the way Program.cs
builds it, the clock is a `FakeTimeProvider`, and every test fails if the job doesn't stop the host.
Assert the outcome (rows migrated, the report's day, the exit code), never only that something exists.
