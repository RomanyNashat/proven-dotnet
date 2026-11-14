using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ClinicQueue.Tests;

public sealed class QueueSnapshotCacheTests
{
    private static readonly DateOnly Day = new(2026, 11, 14);
    private readonly MemoryDistributedCache _store = new(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task GetAsync_AfterSet_ReturnsTheSameSnapshot()
    {
        var cache = new QueueSnapshotCache(_store);
        var snapshot = new QueueSnapshot(3, Day, [1, 2, 5]);
        await cache.SetAsync(snapshot, CancellationToken.None);

        var read = await cache.GetAsync(3, Day, CancellationToken.None);

        read.Should().BeEquivalentTo(snapshot);
    }

    [Fact]
    public async Task GetAsync_NothingCached_ReturnsNull()
    {
        var cache = new QueueSnapshotCache(_store);

        var read = await cache.GetAsync(3, Day, CancellationToken.None);

        read.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_AfterRemove_ReturnsNull()
    {
        var cache = new QueueSnapshotCache(_store);
        await cache.SetAsync(new QueueSnapshot(3, Day, [1]), CancellationToken.None);

        await cache.RemoveAsync(3, Day, CancellationToken.None);

        (await cache.GetAsync(3, Day, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_EntryNotInTheCurrentFormat_ReturnsNull()
    {
        await _store.SetAsync("queue:3:2026-11-14", [0x00, 0x01, 0x00, 0x00, 0x00, 0xFF]);
        var cache = new QueueSnapshotCache(_store);

        var read = await cache.GetAsync(3, Day, CancellationToken.None);

        read.Should().BeNull();
    }
}
