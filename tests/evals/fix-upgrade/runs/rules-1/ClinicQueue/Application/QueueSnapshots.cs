using System.Globalization;
using System.Text.Json;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

/// <summary>Today's queue per clinic, cached for a minute: the waiting-room screens poll it.</summary>
public sealed class QueueSnapshots(QueueDbContext db, IDistributedCache cache)
{
    private static readonly DistributedCacheEntryOptions Lifetime = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };

    /// <summary>The queue positions of a clinic on a day, from the cache or the database.</summary>
    /// <param name="clinicId">The clinic.</param>
    /// <param name="day">The day.</param>
    /// <param name="ct">Cancels the cache and database calls.</param>
    /// <returns>The snapshot, cached for a minute.</returns>
    public async Task<QueueSnapshot> GetAsync(int clinicId, DateOnly day, CancellationToken ct)
    {
        var key = CacheKey(clinicId, day);
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            return Deserialize(cached);
        }

        var positions = await db.Visits.AsNoTracking()
            .Where(v => v.ClinicId == clinicId && v.Day == day)
            .OrderBy(v => v.Position)
            .Select(v => v.Position)
            .ToArrayAsync(ct);
        var snapshot = new QueueSnapshot(clinicId, day, positions);

        await cache.SetAsync(key, Serialize(snapshot), Lifetime, ct);
        return snapshot;
    }

    // The key is data, not text a person reads: a culture such as ar-SA would change its digits and calendar.
    internal static string CacheKey(int clinicId, DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"queue:{clinicId}:{day:yyyy-MM-dd}");

    // System.Text.Json replaces BinaryFormatter, which .NET 9 removed and which allowed remote code execution.
    internal static byte[] Serialize(QueueSnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot);

    internal static QueueSnapshot Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize<QueueSnapshot>(bytes)
        ?? throw new InvalidOperationException("The cached queue snapshot was empty.");
}
