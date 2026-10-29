using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabResults.Infrastructure;

/// <summary>
/// Stamps every new result with the clinician who recorded it. Stateless and a singleton: the caller comes
/// from the context being saved (set on every rental), never from a scoped service captured when the pool's
/// options were built, which stamped the first caller on every row.
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is not LabDbContext db)
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries<LabResult>().Where(e => e.State == EntityState.Added))
        {
            // No caller, no write: a result nobody can be held to is worse than a refused save.
            entry.Property(r => r.RecordedBy).CurrentValue = db.CallerId
                ?? throw new InvalidOperationException("A lab result can't be saved without a signed-in caller.");
        }
    }
}
