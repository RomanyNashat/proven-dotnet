using StackExchange.Redis;

namespace Appointments;

/// <summary>At most 5 OTP SMS per patient per 10 minutes, across replicas.</summary>
public sealed class OtpRateLimiter(IConnectionMultiplexer redis, TimeProvider time)
{
    // Trim, count and add in one step on the server, so concurrent requests can't all pass the check.
    private const string Script = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, ARGV[1] - ARGV[2])
        if redis.call('ZCARD', KEYS[1]) < tonumber(ARGV[3]) then
            redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4])
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            return 1
        end
        return 0
        """;

    public async Task<bool> AllowAsync(int patientId)
    {
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var result = await redis.GetDatabase().ScriptEvaluateAsync(Script,
            [new RedisKey($"otp:{patientId}")],
            [now, (long)TimeSpan.FromMinutes(10).TotalMilliseconds, 5, Guid.NewGuid().ToString("N")]);
        return (int)result == 1;
    }
}
