using System.Runtime.Serialization.Formatters.Binary;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

[Serializable]
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
            using var stream = new MemoryStream(cached);
            return (QueueSnapshot)new BinaryFormatter().Deserialize(stream);
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day.Date)
            .OrderBy(v => v.Position)
            .AsAsyncEnumerable()
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        var snapshot = new QueueSnapshot { ClinicId = clinicId, Day = day.Date, Positions = positions };

        using (var stream = new MemoryStream())
        {
            new BinaryFormatter().Serialize(stream, snapshot);
            await cache.SetAsync(key, stream.ToArray(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) }, ct);
        }

        return snapshot;
    }
}
