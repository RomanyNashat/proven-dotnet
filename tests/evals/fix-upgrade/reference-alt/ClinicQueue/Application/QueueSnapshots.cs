using System.Globalization;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

public sealed record QueueSnapshot(int ClinicId, DateOnly Day, int[] Positions);

/// <summary>Today's queue per clinic, cached for a minute: the waiting-room screens poll it.</summary>
public sealed class QueueSnapshots(QueueDbContext db, IDistributedCache cache)
{
    public async Task<QueueSnapshot> GetAsync(int clinicId, DateOnly day, CancellationToken ct)
    {
        var key = $"queue:{clinicId}:{day:yyyy-MM-dd}";
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null)
        {
            return new QueueSnapshot(clinicId, day, cached.Length == 0 ? [] : [.. cached.Split(',').Select(p => int.Parse(p, CultureInfo.InvariantCulture))]);
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day)
            .OrderBy(v => v.Position)
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        await cache.SetStringAsync(key, string.Join(',', positions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) }, ct);
        return new QueueSnapshot(clinicId, day, positions);
    }
}
