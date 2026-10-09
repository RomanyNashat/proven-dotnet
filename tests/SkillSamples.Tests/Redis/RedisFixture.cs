using StackExchange.Redis;
using Xunit;

namespace SkillSamples.Redis;

/// <summary>A real Redis 7. CI starts redis:7 on localhost:6379.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    public ConnectionMultiplexer Connection { get; private set; } = null!;

    public IDatabase Db => Connection.GetDatabase();

    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    public async Task InitializeAsync() =>
        Connection = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379");

    public async Task DisposeAsync() => await Connection.DisposeAsync();
}
