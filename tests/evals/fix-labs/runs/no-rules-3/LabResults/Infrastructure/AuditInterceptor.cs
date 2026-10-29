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
        Stamp(eventData.Context!);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context!);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<LabResult>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(r => r.RecordedBy).CurrentValue =
                    user.Id ?? throw new InvalidOperationException("No authenticated user to attribute the result to.");
            }
        }
    }
}
