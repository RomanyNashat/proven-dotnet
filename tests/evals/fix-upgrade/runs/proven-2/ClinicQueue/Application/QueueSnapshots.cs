using System.Globalization;
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
        var key = KeyFor(clinicId, day);
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            // BinaryFormatter always throws from .NET 9; entries are JSON now. The cache is in memory and
            // lives a minute, so no entry written in the old format survives a deploy.
            return JsonSerializer.Deserialize<QueueSnapshot>(cached)
                ?? throw new InvalidOperationException($"Cache entry {key} held no snapshot.");
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day.Date)
            .OrderBy(v => v.Position)
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        var snapshot = new QueueSnapshot { ClinicId = clinicId, Day = day.Date, Positions = positions };

        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(snapshot),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) }, ct);

        return snapshot;
    }

    /// <summary>The cache key for a clinic's day; invariant, so it is the same under any server culture.</summary>
    internal static string KeyFor(int clinicId, DateTime day) =>
        string.Create(CultureInfo.InvariantCulture, $"queue:{clinicId}:{day:yyyy-MM-dd}");
}
