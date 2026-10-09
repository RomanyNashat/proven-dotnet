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

### Program.cs
```csharp
// Minimal host — no web server, no Kestrel
var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Services.Configure<JobOptions>(builder.Configuration.GetSection("Job"));

// DI — same as your microservice infrastructure layer
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));   // SQL Server: UseSqlServer

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

// Register the job
builder.Services.AddHostedService<DailyReportJob>();

// Logging
builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("JobName", "daily-report")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

var host = builder.Build();

// Run — the host starts, executes the job, then exits
await host.RunAsync();
```

### Job Implementation
```csharp
public sealed class DailyReportJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<DailyReportJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var exitCode = 0;

        try
        {
            logger.LogInformation("Daily report job started at {Time}", DateTimeOffset.UtcNow);
            var sw = Stopwatch.StartNew();

            await using var scope = scopeFactory.CreateAsyncScope();
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();

            var result = await reportService.GenerateDailyReportAsync(
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                stoppingToken);

            sw.Stop();
            logger.LogInformation(
                "Daily report completed in {ElapsedMs}ms. Records processed: {Count}",
                sw.ElapsedMilliseconds, result.RecordsProcessed);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Daily report job FAILED");
            exitCode = 1;
        }
        finally
        {
            // Signal the host to shut down — the app exits
            lifetime.StopApplication();
            Environment.ExitCode = exitCode;
        }
    }
}
```

### .csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <OutputType>Exe</OutputType>
    </PropertyGroup>
</Project>
```

## Kubernetes CronJob Manifest

### Basic CronJob
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
                - name: ASPNETCORE_ENVIRONMENT
                  value: "Production"
                - name: DOTNET_EnableDiagnostics
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

```csharp
// One-time or repeatable data migration as a console app
public sealed class DataMigrationJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<DataMigrationJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Idempotent migration — safe to run multiple times
            var migrationId = "2026-03-migrate-order-status";
            if (await HasAlreadyRunAsync(context, migrationId, stoppingToken))
            {
                logger.LogInformation("Migration {MigrationId} already completed, skipping",
                    migrationId);
                return;
            }

            logger.LogInformation("Starting migration {MigrationId}", migrationId);

            var batchSize = 1000;
            var totalMigrated = 0;

            while (true)
            {
                var batch = await context.Orders
                    .Where(o => o.LegacyStatus != null && o.Status == OrderStatus.Unknown)
                    .Take(batchSize)
                    .ToListAsync(stoppingToken);

                if (batch.Count == 0) break;

                foreach (var order in batch)
                {
                    order.Status = MapLegacyStatus(order.LegacyStatus!);
                }

                await context.SaveChangesAsync(stoppingToken);
                totalMigrated += batch.Count;

                logger.LogInformation("Migrated {Batch} orders (total: {Total})",
                    batch.Count, totalMigrated);
            }

            await MarkAsCompletedAsync(context, migrationId, totalMigrated, stoppingToken);
            logger.LogInformation("Migration {MigrationId} completed: {Total} records",
                migrationId, totalMigrated);
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    private static async Task<bool> HasAlreadyRunAsync(
        AppDbContext context, string migrationId, CancellationToken ct)
    {
        return await context.DataMigrations
            .AnyAsync(m => m.Id == migrationId && m.CompletedAt != null, ct);
    }

    private static async Task MarkAsCompletedAsync(
        AppDbContext context, string id, int count, CancellationToken ct)
    {
        context.DataMigrations.Add(new DataMigrationRecord
        {
            Id = id,
            RecordsMigrated = count,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync(ct);
    }
}
```

## Cleanup / Retention Job

```csharp
// Common pattern: delete old data on schedule
public sealed class AuditLogRetentionJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<RetentionOptions> options,
    ILogger<AuditLogRetentionJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.RetentionDays);

            // Use ExecuteDeleteAsync for bulk deletion — no entity loading
            var deleted = await context.AuditLogs
                .Where(l => l.CreatedAt < cutoff)
                .ExecuteDeleteAsync(stoppingToken);

            logger.LogInformation(
                "Retention job completed: deleted {Count} audit logs older than {Cutoff}",
                deleted, cutoff);
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
```

## Dockerfile for Console App Jobs

```dockerfile
# Use runtime image (not aspnet — no web server needed)
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/DailyReport.Job/DailyReport.Job.csproj", "src/DailyReport.Job/"]
RUN dotnet restore
COPY . .
RUN dotnet publish "src/DailyReport.Job/DailyReport.Job.csproj" \
    -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DailyReport.Job.dll"]
```

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

```csharp
[Fact]
public async Task DailyReportJob_ProcessesRecords_AndExits()
{
    // Arrange
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connectionString));   // the test container for your engine (testing-integration)
    builder.Services.AddHostedService<DailyReportJob>();
    builder.Services.AddScoped<IReportService, ReportService>();

    var host = builder.Build();

    // Seed test data
    using (var scope = host.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedTestDataAsync(context);
    }

    // Act — run the host (job executes, then stops the host)
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await host.RunAsync(cts.Token);

    // Assert — verify the job produced output
    using (var scope = host.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = await context.Reports.OrderByDescending(r => r.CreatedAt).FirstAsync();
        report.Should().NotBeNull();
        report.RecordsProcessed.Should().BeGreaterThan(0);
    }
}
```
