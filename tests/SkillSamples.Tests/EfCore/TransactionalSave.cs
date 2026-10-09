using Microsoft.EntityFrameworkCore;

namespace SkillSamples.EfCore;

public static class TransactionalSave
{
    /// <summary>
    /// With EnableRetryOnFailure, a transaction opened outside the execution strategy throws. Inside it, the
    /// whole unit runs again after a transient failure. So the unit creates its own entities and calls
    /// SaveChangesAsync itself, as often as it needs, and does only database work (no HTTP, no Kafka).
    /// Each attempt starts with an empty change tracker: entities from a rolled-back attempt would
    /// otherwise look saved and be skipped. Anything tracked before the call is dropped too.
    /// </summary>
    public static Task<T> InTransactionAsync<T>(this DbContext db, Func<CancellationToken, Task<T>> unit, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(
            async attemptCt =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(attemptCt);
                var result = await unit(attemptCt);
                await tx.CommitAsync(attemptCt);
                return result;
            },
            ct);
}
