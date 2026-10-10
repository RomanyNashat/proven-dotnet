using System.Collections.Concurrent;
using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Persistence;
using DotNetCore.CAP.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SkillSamples.Postgres;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Cap;

public sealed class RecordingShipments : IShipments
{
    private readonly ConcurrentDictionary<string, OrderPlaced> _byMessage = new();
    private int _attempts;
    public string? FailOnceFor;

    public int Attempts => Volatile.Read(ref _attempts);
    public IReadOnlyCollection<OrderPlaced> Created => [.. _byMessage.Values];

    public Task<bool> AlreadyHandledAsync(string messageId, CancellationToken ct) => Task.FromResult(_byMessage.ContainsKey(messageId));

    public Task CreateAsync(OrderPlaced order, string messageId, CancellationToken ct)
    {
        Interlocked.Increment(ref _attempts);
        if (order.PatientName == FailOnceFor && Interlocked.Exchange(ref FailOnceFor, null) is not null)
            throw new InvalidOperationException("The shipping system timed out.");
        _byMessage.TryAdd(messageId, order);
        return Task.CompletedTask;
    }
}

[Collection("cap")]   // CAP keeps some state per process; one host at a time
public sealed class CapTests(PgDatabase db) : IClassFixture<PgDatabase>, IAsyncLifetime
{
    private static readonly Uri RabbitMq = new(Environment.GetEnvironmentVariable("RABBITMQ_URL") ?? "amqp://samples:samples@localhost:5672/");
    private readonly string _role = $"cap_app_{Guid.NewGuid():N}";
    private readonly string _group = $"shipping-{Guid.NewGuid():N}";
    private readonly RecordingShipments _shipments = new();
    private string ServiceConnection => new NpgsqlConnectionStringBuilder(db.ConnectionString) { Username = _role, Password = "samples" }.ConnectionString;

    // Stands in for the deploy: CAP's own schema script, run once by a role that may create tables, then
    // a service role that may only read and write them.
    public async Task InitializeAsync()
    {
        await new PostgreSqlStorageInitializer(NullLogger<PostgreSqlStorageInitializer>.Instance,
                Options.Create(new PostgreSqlOptions { ConnectionString = db.ConnectionString, Schema = "cap" }),
                Options.Create(new CapOptions()))
            .InitializeAsync(CancellationToken.None);
        await db.ExecuteAsync("CREATE TABLE IF NOT EXISTS orders (id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY, patient_name varchar(200) NOT NULL)");
        await db.ExecuteAsync($"""
            CREATE ROLE {_role} LOGIN PASSWORD 'samples';
            GRANT USAGE ON SCHEMA cap TO {_role};
            GRANT SELECT, INSERT, UPDATE, DELETE ON cap.published, cap.received, orders TO {_role};
            """);
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await db.ExecuteAsync($"DROP OWNED BY {_role}; DROP ROLE {_role}");
    }

    private IHost Build(Action<IServiceCollection>? change = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddEventBus(ServiceConnection, RabbitMq, _group);
        builder.Services.AddSingleton(NpgsqlDataSource.Create(ServiceConnection));
        builder.Services.AddTransient<PlaceOrder>();
        builder.Services.AddSingleton<IShipments>(_shipments);
        builder.Services.AddTransient<OrderPlacedSubscriber>();
        change?.Invoke(builder.Services);
        return builder.Build();
    }

    private async Task<T> Running<T>(Func<IHost, Task<T>> work)
    {
        using var host = Build();
        await host.StartAsync();
        try { return await work(host); }
        finally { await host.StopAsync(); }
    }

    private static async Task Until(Func<bool> done, TimeSpan within)
    {
        var until = DateTime.UtcNow + within;
        while (!done() && DateTime.UtcNow < until)
            await Task.Delay(200);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AnOrderIsPlaced_ShippingGetsTheEventOnce()
    {
        var id = await Running(async host =>
        {
            var orderId = await host.Services.GetRequiredService<PlaceOrder>().RunAsync("Sara", failBeforeCommit: false, CancellationToken.None);
            await Until(() => _shipments.Created.Any(o => o.OrderId == orderId), TimeSpan.FromSeconds(30));
            return orderId;
        });

        Assert.Single(_shipments.Created, o => o.OrderId == id);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_SomethingFailsAfterTheEventWasWritten_NeitherTheOrderNorTheEventSurvives()
    {
        await Running(async host =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                host.Services.GetRequiredService<PlaceOrder>().RunAsync("Omar", failBeforeCommit: true, CancellationToken.None));
            await Task.Delay(3000);
            return true;
        });

        Assert.Empty(_shipments.Created);
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT count(*) FROM orders WHERE patient_name = 'Omar'"));
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT count(*) FROM cap.published WHERE \"Content\" LIKE '%Omar%'"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ShippingTimesOutTheFirstTime_CapRetriesAndOneShipmentIsCreated()
    {
        _shipments.FailOnceFor = "Huda";

        await Running(async host =>
        {
            await host.Services.GetRequiredService<PlaceOrder>().RunAsync("Huda", failBeforeCommit: false, CancellationToken.None);
            await Until(() => _shipments.Created.Any(o => o.PatientName == "Huda"), TimeSpan.FromSeconds(30));
            return true;
        });

        Assert.Single(_shipments.Created, o => o.PatientName == "Huda");
        Assert.Equal(2, _shipments.Attempts);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheSameMessageArrivesTwice_TheSecondIsANoOp()
    {
        var subscriber = new OrderPlacedSubscriber(_shipments);
        var header = new CapHeader(new Dictionary<string, string?> { [Headers.MessageId] = "message-1" });

        await subscriber.HandleAsync(new OrderPlaced(9, "Ali"), header, CancellationToken.None);
        await subscriber.HandleAsync(new OrderPlaced(9, "Ali"), header, CancellationToken.None);

        Assert.Single(_shipments.Created);
        Assert.Equal(1, _shipments.Attempts);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheSchemaScriptWasntAppliedHere_CapStartsAnyway_TheCheckStopsTheDeploy()
    {
        // Given: an environment where nobody ran the script (CAP's schema there doesn't exist)
        void Missing(IServiceCollection s) => s.Configure<PostgreSqlOptions>(o => o.Schema = "cap_missing");

        // CAP's own initializer can't create it with the service role, logs the error and starts anyway
        using (var plain = Build(s => { Missing(s); s.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>(); }))
        {
            await plain.StartAsync();
            await plain.StopAsync();
        }

        // The check refuses to start
        using var checkedHost = Build(Missing);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => checkedHost.StartAsync());
        Assert.Contains("reviewed CAP schema script", refused.Message);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheDbaReviewsCapsTables_ContentIsUnboundedAndTimesHaveNoZone()
    {
        var types = await db.ScalarAsync<string>("""
            SELECT string_agg(column_name || ':' || data_type, ',' ORDER BY column_name)
            FROM information_schema.columns
            WHERE table_schema = 'cap' AND table_name = 'published' AND column_name IN ('Content', 'Added')
            """);

        Assert.Equal("Added:timestamp without time zone,Content:text", types);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcu_ArabicNamesArriveIntact()
    {
        ProductionConditions.Require();

        await Running(async host =>
        {
            await host.Services.GetRequiredService<PlaceOrder>().RunAsync("سارة أحمد", failBeforeCommit: false, CancellationToken.None);
            await Until(() => _shipments.Created.Any(o => o.PatientName == "سارة أحمد"), TimeSpan.FromSeconds(30));
            return true;
        });

        Assert.Single(_shipments.Created, o => o.PatientName == "سارة أحمد");
    }
}
