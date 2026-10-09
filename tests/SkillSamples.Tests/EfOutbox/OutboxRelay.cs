using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SkillSamples.EfOutbox;

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

// Oldest first, stop at the first failure, and one relay at a time. Several relays with FOR UPDATE SKIP
// LOCKED don't publish a row twice, but they do break the order: one relay holds an appointment's first
// event while another skips past it and publishes the second.
public sealed class OutboxRelay(ClinicDbContext db, IOutboxPublisher publisher, TimeProvider time)
{
    private const long RelayLockKey = 7_410_001;   // any constant, unique per service database
    private const string RelayLockName = "outbox-relay";

    public async Task<int> PublishPendingAsync(int batchSize, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await TryTakeRelayLockAsync(ct))
        {
            return 0;   // another pod is relaying
        }

        var batch = await db.Outbox
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.GetType().Name;   // the type: a message can echo patient data
                break;                                   // keep the order: this one goes first next time
            }

            message.PublishedAt = time.GetUtcNow();
            published++;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return published;
    }

    /// <summary>
    /// A lock owned by the transaction: released at commit or rollback, and by the database if the pod
    /// dies. PostgreSQL: an advisory lock. SQL Server: sp_getapplock (0 or 1 = granted, negative = not).
    /// </summary>
    private async Task<bool> TryTakeRelayLockAsync(CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
        {
            return await db.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({RelayLockKey}) AS \"Value\"")
                .SingleAsync(ct);
        }

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @name, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0",
            [result, new SqlParameter("@name", RelayLockName)], ct);
        return (int)result.Value >= 0;
    }
}
