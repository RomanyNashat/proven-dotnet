using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;
using Xunit;

namespace SkillSamples.Redis;

public sealed class RedisTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    [Fact]
    public async Task Lock_IsExclusive_UntilReleased()
    {
        var locks = new RedisLock(redis.Db);
        var resource = RedisFixture.Unique("report");

        await using (var first = await locks.TryAcquireAsync(resource, TimeSpan.FromSeconds(30)))
        {
            Assert.NotNull(first);
            Assert.Null(await locks.TryAcquireAsync(resource, TimeSpan.FromSeconds(30)));
        }

        await using var again = await locks.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Lock_StaleHolder_CantReleaseOrExtendTheNextHoldersLock()
    {
        var locks = new RedisLock(redis.Db);
        var resource = RedisFixture.Unique("sync");

        var stale = await locks.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(200));
        await Task.Delay(500);   // the TTL runs out while the first holder is still "working"
        await using var current = await locks.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));
        Assert.NotNull(current);

        Assert.False(await stale!.ExtendAsync(TimeSpan.FromSeconds(30)));
        await stale.DisposeAsync();

        Assert.Null(await locks.TryAcquireAsync(resource, TimeSpan.FromSeconds(30)));   // still held by current
        Assert.True(await current.ExtendAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task RateLimit_ConcurrentRequestsInOneMillisecond_ExactlyTheLimitPass()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new SlidingWindowRateLimiter(redis.Db, time);
        var client = RedisFixture.Unique("client");

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => limiter.IsAllowedAsync(client, "otp", limit: 5, TimeSpan.FromMinutes(1))));

        Assert.Equal(5, results.Count(allowed => allowed));
    }

    [Fact]
    public async Task RateLimit_WindowMoves_RoomComesBack()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new SlidingWindowRateLimiter(redis.Db, time);
        var client = RedisFixture.Unique("client");
        var window = TimeSpan.FromMinutes(1);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(await limiter.IsAllowedAsync(client, "otp", 3, window));
        }

        Assert.False(await limiter.IsAllowedAsync(client, "otp", 3, window));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.False(await limiter.IsAllowedAsync(client, "otp", 3, window));   // rejected tries took no slot...
        time.Advance(TimeSpan.FromSeconds(31));
        Assert.True(await limiter.IsAllowedAsync(client, "otp", 3, window));    // ...so the old ones expiring frees room
    }

    [Fact]
    public async Task Stream_EntriesOfACrashedConsumer_AreClaimedByAnother()
    {
        var queue = new StreamWorkQueue(redis.Db, RedisFixture.Unique("stream:appointments"), "reminders");
        await queue.EnsureGroupAsync();
        await queue.EnsureGroupAsync();   // safe to call on every start
        for (var i = 1; i <= 3; i++)
        {
            await queue.AddAsync([new("appointmentId", i)]);
        }

        var readByOldPod = await queue.ReadNewAsync("pod-a-7f9c", 10);
        Assert.Equal(3, readByOldPod.Length);
        await queue.AckAsync(readByOldPod[0].Id);   // then pod-a dies with two unacknowledged

        Assert.Empty(await queue.ReadNewAsync("pod-b-2d1e", 10));   // ">" never redelivers pending entries
        await Task.Delay(300);
        var claimed = await queue.ClaimAbandonedAsync("pod-b-2d1e", TimeSpan.FromMilliseconds(200), 10);

        Assert.Equal(new[] { 2, 3 }, claimed.Select(e => (int)e["appointmentId"]).ToArray());
        foreach (var entry in claimed)
        {
            await queue.AckAsync(entry.Id);
        }

        Assert.Empty(await queue.ClaimAbandonedAsync("pod-b-2d1e", TimeSpan.Zero, 10));
    }
}
