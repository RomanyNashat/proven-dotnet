using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabResults.Infrastructure;

/// <summary>
/// Stamps every new result with the clinician who recorded it. Scoped, with a scoped (not pooled)
/// DbContext: a pooled context resolves its interceptors once from the root provider, which would
/// stamp every result with whichever user (or none) was current at that moment.
/// </summary>
public sealed class AuditInterceptor(CurrentUser user) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        foreach (var entry in context!.ChangeTracker.Entries<LabResult>().Where(e => e.State == EntityState.Added))
        {
            entry.Property(r => r.RecordedBy).CurrentValue = user.Id;
        }
    }
}
