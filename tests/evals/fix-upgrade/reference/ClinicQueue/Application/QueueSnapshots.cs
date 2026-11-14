using System.Text.Json;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

public sealed class QueueSnapshot
{
    public int ClinicId { get; set; }
    public DateTime Day { get; set; }
    public int[] Positions { get; set; } = [];
}

/// <summary>Today's queue per clinic, cached for a minute: the waiting-room screens poll it.</summary>
public sealed class QueueSnapshots(QueueDbContext db, IDistributedCache cache)
{
    public async Task<QueueSnapshot> GetAsync(int clinicId, DateTime day, CancellationToken ct)
    {
        var key = $"queue:{clinicId}:{day:yyyy-MM-dd}";
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            return JsonSerializer.Deserialize<QueueSnapshot>(cached)!;
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day.Date)
            .OrderBy(v => v.Position)
            .AsAsyncEnumerable()
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        var snapshot = new QueueSnapshot { ClinicId = clinicId, Day = day.Date, Positions = positions };

        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(snapshot),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) }, ct);

        return snapshot;
    }
}
