using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

/// <summary>
/// Queue snapshots in the distributed cache as JSON. They were BinaryFormatter blobs on .NET 8; BinaryFormatter
/// always throws from .NET 9 on, and deserializing cache bytes with it was a remote-code-execution risk anyway.
/// </summary>
public sealed class QueueSnapshotCache(IDistributedCache cache)
{
    private static readonly DistributedCacheEntryOptions Lifetime = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };

    /// <summary>The cached snapshot, or null when there is none (or it can't be read, as with an old-format entry).</summary>
    public async Task<QueueSnapshot?> GetAsync(int clinicId, DateOnly day, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(Key(clinicId, day), ct);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QueueSnapshot>(bytes);
        }
        catch (JsonException)
        {
            // An entry this code can't read is a miss: the caller rebuilds it from the database.
            return null;
        }
    }

    /// <summary>Caches the snapshot for a minute.</summary>
    public Task SetAsync(QueueSnapshot snapshot, CancellationToken ct) =>
        cache.SetAsync(Key(snapshot.ClinicId, snapshot.Day), JsonSerializer.SerializeToUtf8Bytes(snapshot), Lifetime, ct);

    /// <summary>Drops the cached snapshot, so the next read sees a booking just made.</summary>
    public Task RemoveAsync(int clinicId, DateOnly day, CancellationToken ct) =>
        cache.RemoveAsync(Key(clinicId, day), ct);

    private static string Key(int clinicId, DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"queue:{clinicId}:{day:yyyy-MM-dd}");
}
