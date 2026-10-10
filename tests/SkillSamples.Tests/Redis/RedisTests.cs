using Microsoft.Extensions.Time.Testing;
using SkillSamples.Production;
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

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_APatientTapsSendOtpOnTwoPhones_ThreePodsLetThreeThrough_AndAMinuteLaterOneMore()
    {
        // Given: three pods, each with its own connection, and an OTP limit of 3 a minute per patient
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        var pods = new List<ConnectionMultiplexer>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                pods.Add(await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379"));
            }

            var limiters = pods.Select(p => new SlidingWindowRateLimiter(p.GetDatabase(), time)).ToList();
            var patient = RedisFixture.Unique("patient");

            // When: nine taps land on the pods in turn, a few milliseconds apart
            var allowed = new List<bool>();
            for (var tap = 0; tap < 9; tap++)
            {
                allowed.Add(await limiters[tap % 3].IsAllowedAsync(patient, "otp", limit: 3, TimeSpan.FromMinutes(1)));
                time.Advance(TimeSpan.FromMilliseconds(5));
            }

            // Then: three pass in total, not three per pod; a minute later there's room again
            Assert.Equal(3, allowed.Count(a => a));
            Assert.Equal(new[] { true, true, true }, allowed.Take(3).ToArray());
            time.Advance(TimeSpan.FromMinutes(1));
            Assert.True(await limiters[1].IsAllowedAsync(patient, "otp", limit: 3, TimeSpan.FromMinutes(1)));
        }
        finally
        {
            foreach (var pod in pods)
            {
                await pod.DisposeAsync();
            }
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheLockAndTheLimiterWork_OnTheRealClock()
    {
        ProductionConditions.Require();
        var locks = new RedisLock(redis.Db);
        var limiter = new SlidingWindowRateLimiter(redis.Db, TimeProvider.System);
        var resource = RedisFixture.Unique("nightly-report");
        var client = RedisFixture.Unique("client");

        await using (var held = await locks.TryAcquireAsync(resource, TimeSpan.FromSeconds(30)))
        {
            Assert.NotNull(held);
            Assert.Null(await locks.TryAcquireAsync(resource, TimeSpan.FromSeconds(30)));
        }

        // The window is Unix milliseconds from TimeProvider: no time zone is involved anywhere.
        var results = new List<bool>();
        for (var i = 0; i < 4; i++)
        {
            results.Add(await limiter.IsAllowedAsync(client, "otp", limit: 3, TimeSpan.FromMinutes(1)));
        }

        Assert.Equal(new[] { true, true, true, false }, results.ToArray());
        await using var again = await locks.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));
        Assert.NotNull(again);
    }
}
