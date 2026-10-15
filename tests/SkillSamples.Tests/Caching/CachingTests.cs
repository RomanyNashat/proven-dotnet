using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SkillSamples.Production;
using StackExchange.Redis;
using Xunit;

namespace SkillSamples.Caching;

public sealed class CachingTests : IAsyncLifetime
{
    private static readonly string Redis = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";
    private readonly List<ServiceProvider> _pods = [];
    private ConnectionMultiplexer _connection = null!;

    private sealed class Store(string name, TimeSpan delay) : IPatientStore
    {
        private int _calls;

        public string Name { get; set; } = name;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<string> LoadNameAsync(int id, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(delay, ct);
            return Name;
        }
    }

    public async Task InitializeAsync() => _connection = await ConnectionMultiplexer.ConnectAsync(Redis);

    public async Task DisposeAsync()
    {
        foreach (var pod in _pods)
        {
            await pod.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }

    // One pod: its own L1, the shared Redis as L2.
    private ServiceProvider Pod(IPatientStore store)
    {
        var pod = new ServiceCollection()
            .AddLogging()
            .AddTwoLevelCache(Redis)
            .AddSingleton(store)
            .AddSingleton<PatientNames>()
            .AddSingleton<IConnectionMultiplexer>(_connection)
            .AddSingleton<L1Eviction>()
            .BuildServiceProvider();
        _pods.Add(pod);
        return pod;
    }

    private static int NewId() => Random.Shared.Next(1, int.MaxValue);

    private static async Task<int> StampedeAsync(ServiceProvider pod, Store store)
    {
        var id = NewId();
        var names = pod.GetRequiredService<PatientNames>();

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => names.NameAsync(id, CancellationToken.None).AsTask()));

        Assert.All(results, name => Assert.Equal("Sara", name));
        return store.Calls;
    }

    [Fact]
    public async Task GetOrCreate_HundredRequestsForAColdKey_OneStoreCall()
    {
        var store = new Store("Sara", TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, await StampedeAsync(Pod(store), store));
    }

    [Fact]
    public async Task RemoveByTag_EvictsEveryEntryWithTheTag()
    {
        var store = new Store("Sara", TimeSpan.Zero);
        var pod = Pod(store);
        var names = pod.GetRequiredService<PatientNames>();
        var id = NewId();
        await names.NameAsync(id, CancellationToken.None);

        await pod.GetRequiredService<HybridCache>().RemoveByTagAsync("patient");
        store.Name = "Sarah";

        Assert.Equal("Sarah", await names.NameAsync(id, CancellationToken.None));
        Assert.Equal(2, store.Calls);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_NameFixedOnOnePod_TheOtherPodServesTheOldOne_UntilTheBackplaneEvictsIt()
    {
        // Given two pods sharing Redis, and pod B has read the patient's name
        var store = new Store("Sara", TimeSpan.Zero);
        var podA = Pod(store);
        var podB = Pod(store);
        var id = NewId();
        Assert.Equal("Sara", await podB.GetRequiredService<PatientNames>().NameAsync(id, CancellationToken.None));

        // When the name is corrected and pod A removes the key (its L1 and Redis)
        store.Name = "Sarah";
        await podA.GetRequiredService<HybridCache>().RemoveAsync(PatientNames.Key(id));

        // Then pod B still answers from its own L1: Redis is right, pod B isn't
        Assert.Equal("Sara", await podB.GetRequiredService<PatientNames>().NameAsync(id, CancellationToken.None));

        // And with the eviction backplane, pod B drops its copy and reads the corrected name
        await podA.GetRequiredService<L1Eviction>().SubscribeAsync();
        await podB.GetRequiredService<L1Eviction>().SubscribeAsync();
        await podA.GetRequiredService<L1Eviction>().InvalidateAsync(PatientNames.Key(id), CancellationToken.None);
        var name = "Sara";
        for (var i = 0; i < 50 && name == "Sara"; i++)
        {
            await Task.Delay(100);
            name = await podB.GetRequiredService<PatientNames>().NameAsync(id, CancellationToken.None);
        }

        Assert.Equal("Sarah", name);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_StampedeProtectionHolds()
    {
        ProductionConditions.Require();
        var store = new Store("Sara", TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, await StampedeAsync(Pod(store), store));
    }
}
