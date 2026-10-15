using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkillSamples.CronJobs;

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
