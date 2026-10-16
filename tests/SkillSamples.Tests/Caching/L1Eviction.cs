using Microsoft.Extensions.Caching.Hybrid;
using StackExchange.Redis;

namespace SkillSamples.Caching;

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
