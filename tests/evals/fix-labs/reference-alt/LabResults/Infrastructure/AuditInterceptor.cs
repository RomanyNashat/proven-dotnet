using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabResults.Infrastructure;

/// <summary>Stamps every new result with the clinician who recorded it. Stateless: one instance serves every
/// pooled context, and the caller comes from the context being saved.</summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is LabDbContext db)
        {
            foreach (var entry in db.ChangeTracker.Entries<LabResult>().Where(e => e.State == EntityState.Added))
            {
                entry.Property(r => r.RecordedBy).CurrentValue = db.CallerId;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
