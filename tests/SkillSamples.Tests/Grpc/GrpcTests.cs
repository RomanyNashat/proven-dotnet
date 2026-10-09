using System.Runtime.CompilerServices;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillSamples.GrpcSamples.V1;
using Xunit;

namespace SkillSamples.GrpcSamples;

public sealed class InMemoryOrderStore : IOrderStore
{
    private readonly Dictionary<int, StoredOrder> _orders = [];
    private int _nextId;

    public bool WatchCancelled { get; private set; }

    public Task<StoredOrder?> FindAsync(int id, CancellationToken ct) => Task.FromResult(_orders.GetValueOrDefault(id));

    public Task<StoredOrder> AddAsync(int customerId, decimal total, DateTimeOffset now, CancellationToken ct)
    {
        var order = new StoredOrder(++_nextId, customerId, OrderStatus.Pending, total, now);
        _orders[order.Id] = order;
        return Task.FromResult(order);
    }

    public async IAsyncEnumerable<OrderStatusChanged> WatchAsync(int id, [EnumeratorCancellation] CancellationToken ct)
    {
        using var registration = ct.Register(() => WatchCancelled = true);
        while (true)
        {
            yield return new OrderStatusChanged { OrderId = id, Status = OrderStatus.Pending };
            await Task.Delay(20, ct);
        }
    }
}

/// <summary>Never answers; records whether the call's token was cancelled.</summary>
public sealed class SlowStore : IOrderStore
{
    public TaskCompletionSource Cancelled { get; } = new();

    public async Task<StoredOrder?> FindAsync(int id, CancellationToken ct)
    {
        using var registration = ct.Register(() => Cancelled.TrySetResult());
        await Task.Delay(Timeout.Infinite, ct);
        return null;
    }

    public Task<StoredOrder> AddAsync(int customerId, decimal total, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();

    public IAsyncEnumerable<OrderStatusChanged> WatchAsync(int id, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>Unavailable for the first <c>failures</c> calls, then answers.</summary>
public sealed class FlakyStore(int failures) : IOrderStore
{
    public int Calls { get; private set; }

    public Task<StoredOrder?> FindAsync(int id, CancellationToken ct) =>
        ++Calls <= failures
            ? throw new RpcException(new Status(StatusCode.Unavailable, "Database restarting."))
            : Task.FromResult<StoredOrder?>(new StoredOrder(id, 1, OrderStatus.Paid, 20m, DateTimeOffset.UnixEpoch));

    public Task<StoredOrder> AddAsync(int customerId, decimal total, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();

    public IAsyncEnumerable<OrderStatusChanged> WatchAsync(int id, CancellationToken ct) => throw new NotSupportedException();
}

public sealed class LeakingStore : IOrderStore
{
    public Task<StoredOrder?> FindAsync(int id, CancellationToken ct) => throw new InvalidOperationException("patient 1089234567 has no file");

    public Task<StoredOrder> AddAsync(int customerId, decimal total, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();

    public IAsyncEnumerable<OrderStatusChanged> WatchAsync(int id, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>The interceptor the skill used to show: it catches everything, the service's own RpcException included.</summary>
public sealed class CatchAllInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex)
        {
            var code = ex switch
            {
                OperationCanceledException => StatusCode.Cancelled,
                _ => StatusCode.Internal,
            };
            throw new RpcException(new Status(code, "An internal error occurred"));
        }
    }
}

public sealed class GrpcTests
{
    // A real Kestrel server on a free localhost port, over HTTP/2: the in-memory TestServer turns gRPC's
    // stream reset at a deadline into an exception the client reports as Internal, which real clients don't see.
    private static async Task<WebApplication> StartAsync(IOrderStore store, Action<GrpcServiceOptions>? grpc = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddGrpc(grpc ?? (o => o.Interceptors.Add<ErrorInterceptor>()));
        var app = builder.Build();
        app.MapGrpcService<OrdersService>();
        await app.StartAsync();
        return app;
    }

    private static Uri AddressOf(WebApplication app) => new(app.Urls.First());

    private static Orders.OrdersClient ClientFor(WebApplication app, Func<IServiceCollection, IHttpClientBuilder>? register = null)
    {
        var services = new ServiceCollection().AddSingleton(TimeProvider.System);
        _ = register?.Invoke(services) ?? services.AddOrdersClient(AddressOf(app));
        return services.BuildServiceProvider().GetRequiredService<Orders.OrdersClient>();
    }

    [Fact]
    public async Task NotFound_FromTheService_ReachesTheClientAsNotFound()
    {
        await using var app = await StartAsync(new InMemoryOrderStore());

        var error = await Assert.ThrowsAsync<RpcException>(() => ClientFor(app).GetOrderAsync(new GetOrderRequest { OrderId = 404 }).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task NotFound_ThroughACatchAllInterceptor_BecomesInternal()
    {
        await using var app = await StartAsync(new InMemoryOrderStore(), o => o.Interceptors.Add<CatchAllInterceptor>());

        var error = await Assert.ThrowsAsync<RpcException>(() => ClientFor(app).GetOrderAsync(new GetOrderRequest { OrderId = 404 }).ResponseAsync);

        Assert.Equal(StatusCode.Internal, error.StatusCode);   // the client can no longer tell "missing" from "broken"
    }

    [Fact]
    public async Task UnexpectedException_IsInternal_WithoutItsMessage()
    {
        await using var app = await StartAsync(new LeakingStore());

        var error = await Assert.ThrowsAsync<RpcException>(() => ClientFor(app).GetOrderAsync(new GetOrderRequest { OrderId = 1 }).ResponseAsync);

        Assert.Equal(StatusCode.Internal, error.StatusCode);
        Assert.DoesNotContain("1089234567", error.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateOrder_RoundTrips_AndRejectsAnEmptyOrder()
    {
        await using var app = await StartAsync(new InMemoryOrderStore());
        var client = ClientFor(app);

        var created = await client.CreateOrderAsync(new CreateOrderRequest { CustomerId = 7, Lines = { new OrderLine { ProductId = 3, Quantity = 2 } } });
        var read = await client.GetOrderAsync(new GetOrderRequest { OrderId = created.Id });
        var empty = await Assert.ThrowsAsync<RpcException>(() => client.CreateOrderAsync(new CreateOrderRequest { CustomerId = 7 }).ResponseAsync);

        Assert.Equal(20m, read.Total.ToDecimal());
        Assert.Equal(OrderStatus.Pending, read.Status);
        Assert.Equal(StatusCode.InvalidArgument, empty.StatusCode);
    }

    [Fact]
    public async Task CallWithoutADeadline_GetsTheDefault_AndTheServerIsCancelledWhenItPasses()
    {
        var store = new SlowStore();
        await using var app = await StartAsync(store, o => o.Interceptors.Add<ErrorInterceptor>());
        var client = ClientFor(app, s => s.AddGrpcClient<Orders.OrdersClient>(o => o.Address = AddressOf(app))
            .AddInterceptor(_ => new DefaultDeadlineInterceptor(TimeProvider.System, TimeSpan.FromMilliseconds(300))));

        var error = await Assert.ThrowsAsync<RpcException>(() => client.GetOrderAsync(new GetOrderRequest { OrderId = 1 }).ResponseAsync);

        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
        await store.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));   // the server's token fired too
    }

    [Fact]
    public async Task Deadline_TwentyCallsInARow_EachReportsDeadlineExceeded()
    {
        await using var app = await StartAsync(new SlowStore(), o => o.Interceptors.Add<ErrorInterceptor>());
        var client = ClientFor(app, s => s.AddGrpcClient<Orders.OrdersClient>(o => o.Address = AddressOf(app))
            .AddInterceptor(_ => new DefaultDeadlineInterceptor(TimeProvider.System, TimeSpan.FromMilliseconds(200))));
        var statuses = new List<StatusCode>();
        var details = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => client.GetOrderAsync(new GetOrderRequest { OrderId = 1 }).ResponseAsync);
            statuses.Add(error.StatusCode);
            details.Add($"{error.StatusCode}: {error.Status.Detail} [{error.Status.DebugException?.GetType().Name}: {error.Status.DebugException?.Message}]".ReplaceLineEndings(" "));
        }

        var counts = string.Join(", ", statuses.GroupBy(s => s).Select(g => $"{g.Key}={g.Count()}"));
        Assert.True(statuses.All(s => s == StatusCode.DeadlineExceeded), $"{counts} :: {string.Join(" || ", details)}");
    }

    [Fact]
    public async Task Stream_ClientCancels_ServerLoopStops()
    {
        var store = new InMemoryOrderStore();
        await using var app = await StartAsync(store);
        using var cts = new CancellationTokenSource();
        using var call = ClientFor(app).WatchOrder(new WatchOrderRequest { OrderId = 1 }, cancellationToken: cts.Token);

        var received = 0;
        var cancelled = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync(cts.Token))
            {
                if (++received == 3)
                {
                    await cts.CancelAsync();
                }
            }
        });

        Assert.Equal(StatusCode.Cancelled, cancelled.StatusCode);
        for (var i = 0; i < 50 && !store.WatchCancelled; i++)
        {
            await Task.Delay(100);
        }

        Assert.True(store.WatchCancelled);
    }

    [Fact]
    public async Task ServiceConfigRetry_RetriesUnavailableReads()
    {
        var store = new FlakyStore(failures: 2);
        await using var app = await StartAsync(store);

        var reply = await ClientFor(app).GetOrderAsync(new GetOrderRequest { OrderId = 5 });

        Assert.Equal(5, reply.Id);
        Assert.Equal(3, store.Calls);
    }

    [Fact]
    public async Task HttpResilienceHandler_DoesNotRetryAGrpcStatus()
    {
        var store = new FlakyStore(failures: 1);
        await using var app = await StartAsync(store);
        // A client with the HTTP resilience pipeline: gRPC answers HTTP 200 with the status in the
        // trailers, so the handler sees a success and never retries.
        var withHttpResilience = ClientFor(app, s =>
        {
            var builder = s.AddGrpcClient<Orders.OrdersClient>(o => o.Address = AddressOf(app));
            builder.AddStandardResilienceHandler();
            return builder;
        });

        var error = await Assert.ThrowsAsync<RpcException>(() => withHttpResilience.GetOrderAsync(new GetOrderRequest { OrderId = 5 }).ResponseAsync);

        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.Equal(1, store.Calls);
    }

    [Theory]
    [InlineData("12.5")]
    [InlineData("-12.5")]
    [InlineData("0.000000001")]
    [InlineData("1234567.89")]
    public void Money_RoundTrips(string text)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var money = amount.ToMoney("SAR");

        Assert.Equal(amount, money.ToDecimal());
        Assert.True(money.Units == 0 || money.Nanos == 0 || Math.Sign(money.Units) == Math.Sign(money.Nanos));
    }
}
