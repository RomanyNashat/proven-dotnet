using System.Globalization;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

/// <summary>Today's queue per clinic, cached for a minute: the waiting-room screens poll it.</summary>
public sealed class QueueSnapshots(QueueDbContext db, IDistributedCache cache)
{
    private static readonly DistributedCacheEntryOptions CacheFor =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };

    /// <summary>The queue of a clinic for a day, from the cache when it is there.</summary>
    /// <param name="clinicId">The clinic.</param>
    /// <param name="day">The day.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The snapshot.</returns>
    public async Task<QueueSnapshot> GetAsync(int clinicId, DateOnly day, CancellationToken ct)
    {
        var key = CacheKey(clinicId, day);
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            return QueueSnapshotCodec.Deserialize(cached);
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day)
            .OrderBy(v => v.Position)
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        var snapshot = new QueueSnapshot(clinicId, day, positions);

        await cache.SetAsync(key, QueueSnapshotCodec.Serialize(snapshot), CacheFor, ct);
        return snapshot;
    }

    internal static string CacheKey(int clinicId, DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"queue:{clinicId}:{day:yyyy-MM-dd}");
}
