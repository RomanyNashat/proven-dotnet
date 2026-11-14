using System.Globalization;
using ClinicQueue.Application;
using ClinicQueue.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ClinicQueue.Tests;

public sealed class QueueSnapshotsTests
{
    [Fact]
    public async Task GetAsync_SnapshotCached_ReturnsCachedSnapshotWithoutDatabase()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var day = new DateOnly(2026, 11, 14);
        var expected = new QueueSnapshot(7, day, [1, 2, 3]);
        await cache.SetAsync("queue:7:2026-11-14", QueueSnapshots.Serialize(expected), new DistributedCacheEntryOptions());
        // Points at a host that doesn't exist: a cache hit must never reach the database.
        var options = new DbContextOptionsBuilder<QueueDbContext>().UseNpgsql("Host=invalid.invalid;Timeout=1").Options;
        await using var db = new QueueDbContext(options);

        var snapshot = await new QueueSnapshots(db, cache).GetAsync(7, day, CancellationToken.None);

        snapshot.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Deserialize_SerializedSnapshot_RoundTrips()
    {
        var snapshot = new QueueSnapshot(3, new DateOnly(2026, 1, 31), [1, 4]);

        var result = QueueSnapshots.Deserialize(QueueSnapshots.Serialize(snapshot));

        result.Should().BeEquivalentTo(snapshot);
    }

    [Fact]
    public void CacheKey_ArabicCulture_UsesInvariantDigitsAndGregorianCalendar()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            QueueSnapshots.CacheKey(5, new DateOnly(2026, 11, 14)).Should().Be("queue:5:2026-11-14");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
