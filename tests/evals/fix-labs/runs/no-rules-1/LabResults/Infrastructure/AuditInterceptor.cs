using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabResults.Infrastructure;

/// <summary>Stamps every new result with the clinician who recorded it.</summary>
public sealed class AuditInterceptor(CurrentUser user) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        foreach (var entry in eventData.Context!.ChangeTracker.Entries<LabResult>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(r => r.RecordedBy).CurrentValue = user.Id;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
