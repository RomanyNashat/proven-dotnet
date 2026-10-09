using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace OrderCleanupWorker;

/// <summary>Deletes cancelled orders older than the retention period, in batches.</summary>
[DisallowConcurrentExecution]
public sealed class CleanupOrdersJob(OrdersDbContext db, IOptions<CleanupOptions> options, TimeProvider time, ILogger<CleanupOrdersJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var cutoff = time.GetUtcNow().AddDays(-options.Value.RetentionDays);
        var total = 0;
        int deleted;
        do
        {
            // OrderLines go with their order (ON DELETE CASCADE in the schema).
            deleted = await db.Orders
                .Where(o => o.Status == OrderStatus.Cancelled && o.CancelledAt < cutoff)
                .OrderBy(o => o.Id)
                .Take(options.Value.BatchSize)
                .ExecuteDeleteAsync(context.CancellationToken);
            total += deleted;
        }
        while (deleted == options.Value.BatchSize);

        logger.LogInformation("Deleted {Count} cancelled orders older than {Cutoff}", total, cutoff);
    }
}
