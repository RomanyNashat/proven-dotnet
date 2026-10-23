using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public static class AlertClaims
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> pending alerts until <paramref name="claimUntil"/>, and
    /// commits the claim at once (one statement). Rows another pod is claiming are skipped, not waited on;
    /// rows another pod holds are left alone until their claim lapses. So each pod texts different patients.
    /// </summary>
    /// <param name="db">The context.</param>
    /// <param name="now">The current time.</param>
    /// <param name="claimUntil">When the claim lapses.</param>
    /// <param name="batchSize">The most rows to claim.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The claimed reading ids.</returns>
    /// <remarks>'Pending' is a literal so the planner can use the partial index ix_readings_alert_pending.</remarks>
    public static Task<List<int>> ClaimDueAsync(
        this SyncDbContext db, DateTimeOffset now, DateTimeOffset claimUntil, int batchSize, CancellationToken ct)
    {
        return db.Database.SqlQuery<int>($"""
            UPDATE readings SET "AlertClaimedUntil" = {claimUntil}
            WHERE "Id" IN (
                SELECT "Id" FROM readings
                WHERE "AlertDelivery" = 'Pending'
                  AND ("AlertClaimedUntil" IS NULL OR "AlertClaimedUntil" < {now})
                ORDER BY "Id"
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);
    }
}
