using System.Globalization;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

/// <summary>Today's queue per clinic, cached for a minute: the waiting-room screens poll it.</summary>
public sealed class QueueSnapshots(QueueDbContext db, IDistributedCache cache)
{
    private static readonly DistributedCacheEntryOptions Lifetime = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };

    public async Task<QueueSnapshot> GetAsync(int clinicId, DateOnly day, CancellationToken ct)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"queue:{clinicId}:{day:yyyy-MM-dd}");
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            return QueueSnapshotSerializer.Deserialize(cached);
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day)
            .OrderBy(v => v.Position)
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        var snapshot = new QueueSnapshot(clinicId, day, positions);

        await cache.SetAsync(key, QueueSnapshotSerializer.Serialize(snapshot), Lifetime, ct);
        return snapshot;
    }
}
