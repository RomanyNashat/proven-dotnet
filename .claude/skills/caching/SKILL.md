---
name: caching
description: Caching for .NET: HybridCache (L1+L2, stampede protection, tag invalidation), cache-aside vs write-back, TTL tiers, the multi-pod L1 gap needing a Redis backplane.
---

# Caching — the discipline, not just a Redis client

How to cache correctly in a .NET service. This is about *strategy* — what to cache, for how long,
how to invalidate, and the traps that bite at scale — not just "call Redis." For raw Redis client
mechanics see `redis-patterns`; this skill is the layer above it.

## HybridCache — the .NET 9/10 default
`HybridCache` (Microsoft.Extensions.Caching.Hybrid) is the modern default. It's a **two-level** cache:
an in-process L1 (fast, per-pod memory) in front of a distributed L2 (Redis, shared across pods).

```csharp
builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new() { Expiration = TimeSpan.FromMinutes(10),
                                    LocalCacheExpiration = TimeSpan.FromMinutes(2) };
});

// One call — checks L1, then L2, then the factory; writes back up both levels.
var patient = await cache.GetOrCreateAsync(
    $"patient:{id}",
    async ct => await repo.GetPatientAsync(id, ct),
    cancellationToken: ct);
```

Why it's the default:
- **Built-in stampede protection.** With plain cache-aside, 100 concurrent requests for a cold key
  all hit the database at once. `HybridCache.GetOrCreateAsync` collapses them into **one** factory
  call; the other 99 await the same result. This alone is a reason to prefer it.
- **Tag-based invalidation.** Tag entries and evict a whole group at once:
  `await cache.RemoveByTagAsync("patient")`.
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
- **Mitigation:** a **Redis pub/sub backplane** — on a write/eviction, publish the key to a channel;
  every pod subscribes and evicts its own L1. (HybridCache does not do this for you across nodes;
  you wire the pub/sub eviction.)
- Or keep L1 TTL short enough that the staleness window is acceptable, and rely on L2 for correctness.
- Know which you're relying on: short-L1-window vs. active-backplane-eviction. Don't assume L1 is
  coherent across pods — it isn't.

## Rules
- Prefer `HybridCache` for new caching — you get stampede protection and tag invalidation for free.
- Never cache without a deliberate TTL and a deliberate invalidation story.
- On multi-pod services, decide the L1-coherence approach explicitly (backplane or short window).
- Don't cache writes you can't afford to lose with write-back.
- Cache keys: stable, prefixed, versioned when the shape changes (`patient:v2:{id}`).
