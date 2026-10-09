using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SkillSamples.EfCore;

/// <summary>
/// Stateless, so one instance serves every pooled context. The user comes from the context being saved,
/// never from a scoped service captured when the pool was built (that would stamp one user on every save).
/// </summary>
public sealed class AuditInterceptor(TimeProvider time) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is VisitsDbContext db)
        {
            var now = time.GetUtcNow();
            foreach (var entry in db.ChangeTracker.Entries<IAudited>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Property(nameof(IAudited.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAudited.CreatedBy)).CurrentValue = db.CallerId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(IAudited.UpdatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAudited.UpdatedBy)).CurrentValue = db.CallerId;
                }
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
