using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkillSamples.CronJobs;

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
