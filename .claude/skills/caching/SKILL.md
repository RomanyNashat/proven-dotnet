---
name: caching
description: Caching for .NET: HybridCache (L1+L2, stampede protection, tag invalidation), cache-aside vs write-back, TTL tiers, the multi-pod L1 gap and the Redis pub/sub eviction that closes it. Tested against real Redis.
version: 2.0.0
---

# Caching — the discipline, not just a Redis client

How to cache correctly in a .NET service. This is about *strategy* — what to cache, for how long,
how to invalidate, and the traps that bite at scale — not just "call Redis." For raw Redis client
mechanics see `redis-patterns`; this skill is the layer above it.

## HybridCache — the .NET 9/10 default
`HybridCache` (Microsoft.Extensions.Caching.Hybrid) is the modern default. It's a **two-level** cache:
an in-process L1 (fast, per-pod memory) in front of a distributed L2 (Redis, shared across pods).

<!-- sample: tests/SkillSamples.Tests/Caching/CachingSetup.cs -->
```csharp
public static class CachingSetup
{
    public static IServiceCollection AddTwoLevelCache(this IServiceCollection services, string redisConnectionString)
    {
        services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);   // L2: shared by every pod
        services.AddHybridCache(o => o.DefaultEntryOptions = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(10),            // L2
            LocalCacheExpiration = TimeSpan.FromMinutes(2),   // L1, per pod: shorter than L2
        });
        return services;
    }
}
```

<!-- sample: tests/SkillSamples.Tests/Caching/PatientNames.cs -->
```csharp
public sealed class PatientNames(HybridCache cache, IPatientStore store)
{
    public static string Key(int id) => $"patient:v1:{id}";   // prefixed and versioned: a new shape gets a new key

    // L1, then L2, then the store, with one store call however many requests arrive for a cold key.
    public ValueTask<string> NameAsync(int id, CancellationToken ct) =>
        cache.GetOrCreateAsync(Key(id), async token => await store.LoadNameAsync(id, token), tags: ["patient"], cancellationToken: ct);
}
```

Why it's the default:
- **Built-in stampede protection.** With plain cache-aside, 100 concurrent requests for a cold key
  all hit the database at once. `HybridCache.GetOrCreateAsync` collapses them into **one** factory
  call; the other 99 await the same result (tested: 100 requests, one store call, also on a slim image).
- **Tag-based invalidation.** Tag entries and evict a whole group at once:
  `await cache.RemoveByTagAsync("patient")` (tested).
- **L1+L2 in one API** — you don't hand-write the "check memory, then Redis, then DB" ladder.

## The strategies (pick per data shape)
- **Cache-aside (lazy)** — the default. Read: check cache → miss → load → populate. `GetOrCreateAsync`
  is this, done right. Best for read-heavy data that tolerates slight staleness.
- **Read-through** — the cache itself loads on miss (HybridCache's factory is effectively this).
- **Write-back / write-behind** — write to cache, flush to the store asynchronously. Fast writes,
  but risk of loss on crash — only for data you can afford to lose or reconstruct.
- **Write-through** — write to cache and store together. Consistent, slower writes.

## TTL tiers (don't give everything one expiry)
Match TTL to how fast the data changes and how bad stale is:
- Near-static reference data (code tables, config) → long (hours), evict on change.
- User/session-scoped read models → medium (minutes).
- Volatile/derived data (counts, feeds) → short (seconds–minute), or don't cache.
Set L1 (LocalCacheExpiration) shorter than L2 so per-pod memory turns over faster than the shared
layer.

## The scale trap: L1 doesn't sync across pods
The one that bites in production. HybridCache's **L1 is per-pod** — evicting or updating a key on
pod A does **not** evict L1 on pods B and C. So after a write, other pods can serve stale L1 for up
to the L1 TTL, even though L2 (Redis) is correct.
- **Tested:** two pods share Redis; pod A removes a corrected patient's key; pod B keeps answering
  the old name from its L1.
- **Mitigation:** a Redis pub/sub eviction. On a write, remove the key and publish it; every pod
  subscribes and removes its own L1 copy. HybridCache doesn't do this across nodes. Tested: with it, pod
  B reads the corrected name within a moment.

<!-- sample: tests/SkillSamples.Tests/Caching/L1Eviction.cs -->
```csharp
/// <summary>
/// HybridCache's L1 is per pod: removing a key on one pod leaves the others serving their copy until it
/// expires. This tells every pod to drop its copy, over Redis pub/sub.
/// </summary>
public sealed class L1Eviction(IConnectionMultiplexer redis, HybridCache cache)
{
    private static readonly RedisChannel Channel = RedisChannel.Literal("hybrid-cache:evict");

    /// <summary>Every pod subscribes once, at start.</summary>
    public async Task SubscribeAsync()
    {
        var queue = await redis.GetSubscriber().SubscribeAsync(Channel);
        queue.OnMessage(async message => await cache.RemoveAsync(message.Message.ToString()));
    }

    /// <summary>After a write: drop the key here and in L2, then tell the other pods to drop theirs.</summary>
    public async Task InvalidateAsync(string key, CancellationToken ct)
    {
        await cache.RemoveAsync(key, ct);
        await redis.GetSubscriber().PublishAsync(Channel, key);
    }
}
```

- Or keep L1 TTL short enough that the staleness window is acceptable, and rely on L2 for correctness.
- Know which you're relying on: short-L1-window vs. active-backplane-eviction. Don't assume L1 is
  coherent across pods — it isn't.

## Rules
- Prefer `HybridCache` for new caching — you get stampede protection and tag invalidation for free.
- Never cache without a deliberate TTL and a deliberate invalidation story.
- On multi-pod services, decide the L1-coherence approach explicitly (backplane or short window).
- Don't cache writes you can't afford to lose with write-back.
- Cache keys: stable, prefixed, versioned when the shape changes (`patient:v2:{id}`).
