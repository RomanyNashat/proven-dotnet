using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabResults.Infrastructure;

/// <summary>
/// Stamps every new result with the clinician who recorded it. Stateless, so one instance serves every
/// pooled context: the caller comes from the context being saved, never from a scoped service captured
/// when the pool's options were built (that stamped the first caller on every result).
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

    internal static void Stamp(DbContext? context)
    {
        if (context is not LabDbContext db)
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries<LabResult>().Where(e => e.State == EntityState.Added))
        {
            entry.Property(r => r.RecordedBy).CurrentValue = db.CallerId
                ?? throw new InvalidOperationException("A lab result can only be recorded by a signed-in clinician.");
        }
    }
}
