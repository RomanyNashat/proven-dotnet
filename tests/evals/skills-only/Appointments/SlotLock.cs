using StackExchange.Redis;

namespace Appointments;

/// <summary>One booking per slot at a time, across all replicas.</summary>
public sealed class SlotLock(IConnectionMultiplexer redis)
{
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(10);

    public Task<bool> TryTakeAsync(string slot) =>
        redis.GetDatabase().StringSetAsync($"locks:slot:{slot}", Environment.MachineName, Hold, When.NotExists);

    public Task ReleaseAsync(string slot) =>
        redis.GetDatabase().KeyDeleteAsync($"locks:slot:{slot}");
}
