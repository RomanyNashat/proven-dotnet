---
name: redis-patterns
description: Redis with StackExchange.Redis: cache-aside, a token-checked lock, an atomic sliding-window rate limiter, streams with claim of abandoned entries, leaderboards. Core code tested in CI.
version: 1.1.0
---

# Redis Patterns

## Connection Management

### Singleton ConnectionMultiplexer (CRITICAL)
```csharp
// ConnectionMultiplexer is thread-safe and MUST be singleton
// Creating per-request causes connection storms and kills Redis

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = ConfigurationOptions.Parse(
        builder.Configuration.GetConnectionString("Redis")!);
    config.AbortOnConnectFail = false;  // retry in background
    config.ConnectTimeout = 5000;
    config.SyncTimeout = 3000;
    config.AsyncTimeout = 5000;
    config.ConnectRetry = 3;
    config.DefaultDatabase = 0;
    config.ClientName = "OrderService";
    config.ReconnectRetryPolicy = new ExponentialRetry(5000);

    return ConnectionMultiplexer.Connect(config);
});

// Register IDatabase for convenience
builder.Services.AddSingleton<IDatabase>(sp =>
    sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
```

### IDistributedCache (ASP.NET Core integration)
```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "app:";  // key prefix
});

// Usage with IDistributedCache
public sealed class CachedOrderService(
    IDistributedCache cache,
    IOrderRepository repository,
    ILogger<CachedOrderService> logger)
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
        SlidingExpiration = TimeSpan.FromMinutes(5)
    };

    public async Task<OrderDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var cacheKey = $"orders:{id}";

        var cached = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<OrderDto>(cached);

        var order = await repository.GetByIdAsync(id, ct);
        if (order is null) return null;

        await cache.SetStringAsync(cacheKey,
            JsonSerializer.Serialize(order), CacheOptions, ct);

        return order;
    }

    public async Task InvalidateAsync(int id, CancellationToken ct)
    {
        await cache.RemoveAsync($"orders:{id}", ct);
    }
}
```

## Key Naming Conventions

```
Pattern: {service}:{entity}:{identifier}:{field}

Examples:
  orders:cache:{orderId}           — cached order data
  orders:count:pending             — counter for pending orders
  users:session:{sessionId}        — user session data
  challenges:leaderboard:{id}      — sorted set for leaderboard
  notifications:unread:{userId}    — unread notification count
  locks:order-processing:{orderId} — distributed lock
  rate:api:{clientId}:{endpoint}   — rate limiter counter
```

Rules:
- Use colons `:` as separators (Redis convention, enables key scanning by prefix)
- Keep keys short — long keys waste memory and bandwidth
- Include the entity type for clarity
- Use `SCAN` pattern matching: `SCAN 0 MATCH orders:cache:*`

## Cache-Aside Pattern (standard pattern)

```csharp
public sealed class RedisCacheService(IDatabase db, ILogger<RedisCacheService> logger)
{
    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan expiry,
        CancellationToken ct) where T : class
    {
        // 1. Check cache
        var cached = await db.StringGetAsync(key);
        if (cached.HasValue)
        {
            return JsonSerializer.Deserialize<T>(cached!);
        }

        // 2. Cache miss — load from source
        var value = await factory(ct);
        if (value is null) return null;

        // 3. Store in cache
        var serialized = JsonSerializer.Serialize(value);
        await db.StringSetAsync(key, serialized, expiry);

        return value;
    }

    public async Task InvalidateByPrefixAsync(string prefix)
    {
        var server = db.Multiplexer.GetServer(db.Multiplexer.GetEndPoints().First());
        var keys = server.Keys(pattern: $"{prefix}*").ToArray();

        if (keys.Length > 0)
        {
            await db.KeyDeleteAsync(keys);
            logger.LogInformation("Invalidated {Count} keys with prefix {Prefix}",
                keys.Length, prefix);
        }
    }
}
```

Two limits of this version: a value that doesn't exist (`null`) isn't cached, so every request for a
missing id reaches the database; and a hot key that expires sends every concurrent request to the
database at once. HybridCache (`caching`) handles both. `InvalidateByPrefixAsync` scans one endpoint
only (wrong on a cluster) and is slow on a large keyspace; prefer versioned keys (`orders:v7:{id}`) and
bump the version.

## Distributed Locks

### Using RedLock.net
```csharp
// Registration
builder.Services.AddSingleton<IDistributedLockFactory>(sp =>
{
    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
    return RedLockFactory.Create(new List<RedLockMultiplexer>
    {
        new(multiplexer)
    });
});

// Usage — prevent concurrent processing of the same order
public sealed class OrderProcessor(IDistributedLockFactory lockFactory)
{
    public async Task<bool> ProcessOrderAsync(int orderId, CancellationToken ct)
    {
        var lockKey = $"locks:order-processing:{orderId}";
        var expiry = TimeSpan.FromMinutes(2);
        var wait = TimeSpan.FromSeconds(10);
        var retry = TimeSpan.FromSeconds(1);

        await using var redLock = await lockFactory.CreateLockAsync(
            lockKey, expiry, wait, retry, ct);

        if (!redLock.IsAcquired)
        {
            // Another instance is processing this order
            return false;
        }

        // Safe to process — we hold the lock
        await DoProcessingAsync(orderId, ct);
        return true;
    }
    // Lock is auto-released when redLock is disposed
}
```

### A lock on one Redis instance (tested)

<!-- sample: tests/SkillSamples.Tests/Redis/RedisLock.cs -->
```csharp
// A lock on one Redis instance. Good for "don't run this twice at once" (a report, a sync job). Not a
// guarantee for money or bookings: if the holder pauses longer than the TTL, or Redis fails over to a
// replica that hadn't received the key, two holders can overlap. Those need a database constraint.
public sealed class RedisLock(IDatabase db)
{
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @token then return redis.call('del', @key) else return 0 end");

    private static readonly LuaScript ExtendScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @token then return redis.call('pexpire', @key, @ttlMs) else return 0 end");

    public async Task<Handle?> TryAcquireAsync(string resource, TimeSpan ttl)
    {
        RedisKey key = $"locks:{resource}";
        var token = Guid.NewGuid().ToString("N");   // only the holder knows it
        return await db.StringSetAsync(key, token, ttl, When.NotExists) ? new Handle(db, key, token) : null;
    }

    public sealed class Handle(IDatabase db, RedisKey key, string token) : IAsyncDisposable
    {
        // For work that can outlast the TTL: extend before it runs out. False means the lock was lost.
        public async Task<bool> ExtendAsync(TimeSpan ttl) =>
            (long)await db.ScriptEvaluateAsync(ExtendScript, new { key, token, ttlMs = (long)ttl.TotalMilliseconds }) == 1;

        // Deletes the key only if it still holds our token. A plain DEL after the TTL expired would delete
        // the next holder's lock.
        public async ValueTask DisposeAsync() => await db.ScriptEvaluateAsync(ReleaseScript, new { key, token });
    }
}
```

```csharp
await using var handle = await locks.TryAcquireAsync($"monthly-report:{clinicId}", TimeSpan.FromMinutes(2));
if (handle is null)
{
    return;   // another pod is running it
}

await BuildReportAsync(clinicId, ct);
```

The release checks the token because a plain `DEL` after the TTL ran out deletes the **next** holder's
lock; a test in CI shows the stale holder failing to release or extend it.

## Pub/Sub (real-time notifications)

```csharp
// Publisher
public sealed class RedisEventPublisher(IConnectionMultiplexer redis)
{
    private readonly ISubscriber _subscriber = redis.GetSubscriber();

    public async Task PublishOrderUpdatedAsync(int orderId, OrderStatus newStatus)
    {
        var message = JsonSerializer.Serialize(new OrderUpdatedEvent(orderId, newStatus));
        await _subscriber.PublishAsync(
            RedisChannel.Literal("events:order-updated"), message);
    }
}

// Subscriber (in a BackgroundService)
public sealed class OrderUpdateSubscriber(
    IConnectionMultiplexer redis,
    ILogger<OrderUpdateSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var subscriber = redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal("events:order-updated"),
            (channel, message) =>
            {
                var evt = JsonSerializer.Deserialize<OrderUpdatedEvent>(message!);
                logger.LogInformation("Order {OrderId} updated to {Status}",
                    evt!.OrderId, evt.Status);
                // Push to SignalR, update local cache, etc.
            });

        // Keep alive until cancellation
        await Task.Delay(Timeout.Infinite, ct);
    }
}
```

## Sorted Sets (leaderboards, rankings)

```csharp
public sealed class LeaderboardService(IDatabase db)
{
    private const string Key = "challenges:leaderboard:steps";

    // Add or update score
    public async Task UpdateScoreAsync(int userId, double score)
    {
        await db.SortedSetAddAsync(Key, userId.ToString(), score);
    }

    // Increment score atomically
    public async Task<double> IncrementScoreAsync(int userId, double increment)
    {
        return await db.SortedSetIncrementAsync(Key, userId.ToString(), increment);
    }

    // Get top N (highest scores)
    public async Task<IReadOnlyList<LeaderboardEntry>> GetTopAsync(int count)
    {
        var entries = await db.SortedSetRangeByRankWithScoresAsync(
            Key, 0, count - 1, Order.Descending);

        return entries.Select((e, i) => new LeaderboardEntry
        {
            Rank = i + 1,
            UserId = (int)e.Element,
            Score = e.Score
        }).ToList();
    }

    // Get user's rank (0-based, descending)
    public async Task<long?> GetRankAsync(int userId)
    {
        return await db.SortedSetRankAsync(Key, userId.ToString(), Order.Descending);
    }

    // Get users within score range
    public async Task<IReadOnlyList<LeaderboardEntry>> GetByScoreRangeAsync(
        double minScore, double maxScore)
    {
        var entries = await db.SortedSetRangeByScoreWithScoresAsync(
            Key, minScore, maxScore, order: Order.Descending);

        return entries.Select(e => new LeaderboardEntry
        {
            UserId = (int)e.Element,
            Score = e.Score
        }).ToList();
    }

    // Remove expired entries (score-based TTL)
    public async Task<long> RemoveExpiredAsync(double cutoffScore)
    {
        return await db.SortedSetRemoveRangeByScoreAsync(Key, 0, cutoffScore);
    }
}
```

## Hash Sets (structured objects)

```csharp
// Store structured data without serialization overhead
public async Task SetUserSessionAsync(Guid sessionId, UserSession session)
{
    var key = $"users:session:{sessionId}";
    var entries = new HashEntry[]
    {
        new("userId", session.UserId.ToString()),
        new("email", session.Email),
        new("role", session.Role),
        new("loginAt", session.LoginAt.ToUnixTimeSeconds()),
        new("tenantId", session.TenantId.ToString())
    };

    await db.HashSetAsync(key, entries);
    await db.KeyExpireAsync(key, TimeSpan.FromHours(8));
}

// Get specific fields without loading entire object
public async Task<string?> GetUserRoleAsync(Guid sessionId)
{
    return await db.HashGetAsync($"users:session:{sessionId}", "role");
}

// Increment a specific field atomically
public async Task<long> IncrementRequestCountAsync(Guid sessionId)
{
    return await db.HashIncrementAsync($"users:session:{sessionId}", "requestCount");
}
```

## Rate Limiting with Redis (tested)

<!-- sample: tests/SkillSamples.Tests/Redis/SlidingWindowRateLimiter.cs -->
```csharp
// A limit shared by every pod (ASP.NET Core's built-in rate limiter counts per pod). One Lua script, so
// "is there room?" and "take a slot" happen together: two requests can't both see the last free slot.
// Rejected requests don't take a slot, so a client that backs off gets back in when the window moves.
public sealed class SlidingWindowRateLimiter(IDatabase db, TimeProvider time)
{
    private static readonly LuaScript Script = LuaScript.Prepare("""
        redis.call('zremrangebyscore', @key, '-inf', @windowStart)
        if redis.call('zcard', @key) < tonumber(@limit) then
            redis.call('zadd', @key, @now, @member)
            redis.call('pexpire', @key, @windowMs)
            return 1
        end
        return 0
        """);

    public async Task<bool> IsAllowedAsync(string clientId, string endpoint, int limit, TimeSpan window)
    {
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var windowMs = (long)window.TotalMilliseconds;
        var allowed = await db.ScriptEvaluateAsync(Script, new
        {
            key = (RedisKey)$"rate:{endpoint}:{clientId}",
            now,
            windowStart = now - windowMs,
            limit,
            windowMs,
            member = $"{now}:{Guid.NewGuid():N}",   // unique: two requests in the same millisecond both count
        });
        return (long)allowed == 1;
    }
}
```

The version this replaced had three bugs a test catches: two requests in the same millisecond counted as
one (the member was the timestamp), it let one request over the limit (it compared the count before
adding), and it read and wrote in separate steps, so concurrent requests could all pass. Get "now" from
`TimeProvider`, which also makes the window testable.

Tested as a story: a patient taps "send OTP" nine times across three pods, each with its own connection.
Three get through in total, not three per pod, and a minute later there's room again. On a slim image the
lock and the limiter work unchanged: the window is Unix milliseconds, so no time zone is involved.

## Redis Streams (lightweight event streaming)

### When to Use Streams vs Pub/Sub vs Kafka
- **Pub/Sub**: fire-and-forget, no persistence, no replay, no consumer groups
- **Streams**: persistent, consumer groups, replay, acknowledgment — lightweight Kafka alternative
- **Kafka**: massive throughput, multi-datacenter replication, long-term retention, schema registry

### Produce to a stream
```csharp
public async Task PublishOrderEventAsync(int orderId, string eventType, string data)
{
    var entries = new NameValueEntry[]
    {
        new("orderId", orderId.ToString()),
        new("eventType", eventType),
        new("data", data),
        new("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
    };

    // XADD — auto-generated ID (timestamp-based)
    var messageId = await db.StreamAddAsync("stream:orders", entries, maxLength: 100_000, useApproximateMaxLength: true);
}
```

### Consumer group (at-least-once, tested)

<!-- sample: tests/SkillSamples.Tests/Redis/StreamWorkQueue.cs -->
```csharp
// A Redis Stream read by a consumer group: each entry goes to one consumer, and stays pending until it's
// acknowledged.
public sealed class StreamWorkQueue(IDatabase db, RedisKey stream, string group)
{
    public async Task EnsureGroupAsync()
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(stream, group, StreamPosition.Beginning, createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
        {
            // the group already exists
        }
    }

    public Task<RedisValue> AddAsync(NameValueEntry[] fields) =>
        db.StreamAddAsync(stream, fields, maxLength: 100_000, useApproximateMaxLength: true);   // bounded

    public Task<StreamEntry[]> ReadNewAsync(string consumer, int count) =>
        db.StreamReadGroupAsync(stream, group, consumer, StreamPosition.NewMessages, count);

    public Task AckAsync(RedisValue id) => db.StreamAcknowledgeAsync(stream, group, id);

    // Entries a consumer read and never acknowledged (it crashed). The consumer name is the pod name,
    // which changes on every deploy, so a restarted pod never sees its old pending entries: any live
    // consumer claims them once they've been idle long enough. Needs Redis 6.2+.
    public async Task<StreamEntry[]> ClaimAbandonedAsync(string consumer, TimeSpan minIdle, int count) =>
        (await db.StreamAutoClaimAsync(stream, group, consumer, (long)minIdle.TotalMilliseconds, "0-0", count)).ClaimedEntries;
}
```

The worker loop: on start, `EnsureGroupAsync`; then repeatedly `ClaimAbandonedAsync` (say, entries idle
over 5 minutes), then `ReadNewAsync`, process each entry, `AckAsync` it. Not acknowledging on failure
leaves it pending, and the claim step retries it later. Count deliveries (`XPENDING` reports them) and
move an entry that keeps failing to a dead-letter stream, or it's claimed forever.

### Trim streams (prevent unbounded growth)
```csharp
// Keep only last 10,000 entries (approximate trimming)
await db.StreamTrimAsync("stream:orders", 10000, useApproximateMaxLength: true);

// Or use MAXLEN during XADD
await db.StreamAddAsync("stream:orders", entries, maxLength: 10000, useApproximateMaxLength: true);
```

## Performance Tips

- **SCAN over KEYS**: `KEYS *` blocks Redis. Use `SCAN 0 MATCH pattern* COUNT 100` for production.
- **Pipeline commands**: Use `IBatch` or `ITransaction` to send multiple commands in one roundtrip.
- **FireAndForget**: Use `CommandFlags.FireAndForget` for non-critical writes (counters, analytics).
- **Avoid large values**: Keep values under 100KB. For larger objects, split across multiple keys.
- **Use EXPIRE**: Every key should have a TTL. Redis is not a permanent data store.
- **Monitor memory**: `INFO memory` — watch `used_memory` and `mem_fragmentation_ratio`.

```csharp
// Fire and forget — don't await, don't care about result
await db.StringIncrementAsync("metrics:page-views:home", flags: CommandFlags.FireAndForget);

// Batch — multiple commands, one roundtrip
var batch = db.CreateBatch();
var task1 = batch.StringGetAsync("key1");
var task2 = batch.StringGetAsync("key2");
var task3 = batch.StringGetAsync("key3");
batch.Execute();

var val1 = await task1;
var val2 = await task2;
var val3 = await task3;
```

## See also — caching layer and SignalR
- **HybridCache** (.NET 9/10) is the modern default for read caching — L1+L2, stampede protection,
  tag invalidation — and it sits on top of Redis as its L2. For caching *strategy* (TTL tiers,
  cache-aside vs write-back, the multi-pod L1 invalidation gap), see the `caching` skill. StackExchange.Redis
  here is the raw client; `caching` is the discipline above it.
- **SignalR backplane** — Redis is also the SignalR scale-out backplane (`AddStackExchangeRedis`).
  See the `signalr` per-project skill (copy it in if you use hubs); multi-pod needs the backplane AND sticky sessions.
