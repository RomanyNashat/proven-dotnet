using StackExchange.Redis;

namespace SkillSamples.Redis;

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
