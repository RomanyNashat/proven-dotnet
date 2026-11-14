using System.Globalization;
using System.Text.Json;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

public sealed record QueueSnapshot(int ClinicId, DateOnly Day, int[] Positions);

/// <summary>Today's queue per clinic, cached for a minute: the waiting-room screens poll it.</summary>
public sealed class QueueSnapshots(QueueDbContext db, IDistributedCache cache)
{
    // BinaryFormatter is gone from .NET 9 on (it always throws), and was a deserialization risk before that.
    public static byte[] Serialize(QueueSnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot);

    public static QueueSnapshot? Deserialize(byte[] bytes) => JsonSerializer.Deserialize<QueueSnapshot>(bytes);

    public async Task<QueueSnapshot> GetAsync(int clinicId, DateOnly day, CancellationToken ct)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"queue:{clinicId}:{day:yyyy-MM-dd}");
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null && Deserialize(cached) is { } hit)
        {
            return hit;
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day)
            .OrderBy(v => v.Position)
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        var snapshot = new QueueSnapshot(clinicId, day, positions);

        await cache.SetAsync(key, Serialize(snapshot),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) }, ct);

        return snapshot;
    }
}
