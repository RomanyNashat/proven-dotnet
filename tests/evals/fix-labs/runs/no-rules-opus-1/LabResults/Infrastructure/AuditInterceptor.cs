using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabResults.Infrastructure;

/// <summary>Stamps every new result with the clinician who recorded it.</summary>
/// <remarks>
/// Registered as a singleton and shared by pooled contexts, so it must not capture the user at construction:
/// it asks <see cref="CurrentUser"/> at save time.
/// </remarks>
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
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<LabResult>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(r => r.RecordedBy).CurrentValue = user.Id
                    ?? throw new InvalidOperationException("A lab result can only be recorded by an authenticated clinician.");
            }
        }
    }
}
