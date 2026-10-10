using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SkillSamples.Compliance;

// Adds the audit rows to the same SaveChanges, so a change and its audit commit or roll back together:
// no audit row for a change that failed, and no change without one. The record's id has to be known
// before the save, so PHI entities take their keys from a HiLo sequence (`efcore-patterns`).
public sealed class PhiAuditInterceptor(ICurrentUser user, TimeProvider time) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditRows(eventData.Context!);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        AddAuditRows(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    private void AddAuditRows(DbContext context)
    {
        var at = time.GetUtcNow();
        var rows = context.ChangeTracker.Entries<IPhiRecord>()   // runs DetectChanges first
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => new PhiAuditRow
            {
                RecordType = e.Metadata.ClrType.Name,
                RecordId = e.Entity.Id,
                Action = e.State.ToString(),
                // Column names only: the values are patient data, and more people read the audit than the record.
                ChangedColumns = e.State == EntityState.Modified
                    ? string.Join(',', e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name))
                    : "",
                UserId = user.Id,
                At = at
            })
            .ToList();   // before AddRange changes what the tracker holds

        context.AddRange(rows);
    }
}
