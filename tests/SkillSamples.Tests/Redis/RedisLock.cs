using StackExchange.Redis;

namespace SkillSamples.Redis;

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
