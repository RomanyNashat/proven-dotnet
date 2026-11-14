using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application;

/// <summary>Today's queue per clinic, cached for a minute: the waiting-room screens poll it.</summary>
public sealed class QueueSnapshots(QueueDbContext db, QueueSnapshotCache cache)
{
    /// <summary>The clinic's queue for the day, from the cache when it's there.</summary>
    public async Task<QueueSnapshot> GetAsync(int clinicId, DateOnly day, CancellationToken ct)
    {
        var cached = await cache.GetAsync(clinicId, day, ct);
        if (cached is not null)
        {
            return cached;
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day)
            .OrderBy(v => v.Position)
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        var snapshot = new QueueSnapshot(clinicId, day, positions);

        await cache.SetAsync(snapshot, ct);
        return snapshot;
    }
}
