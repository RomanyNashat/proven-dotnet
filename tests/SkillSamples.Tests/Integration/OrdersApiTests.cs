using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SkillSamples.OrdersApi;
using Xunit;

namespace SkillSamples.Integration;

/// <summary>The same checks on both engines.</summary>
public abstract class OrdersApiTests<TFactory>(TFactory factory) : IAsyncLifetime
    where TFactory : OrdersApiFactory
{
    protected TFactory Api => factory;

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient ClientFor(int userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return client;
    }

    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsCreatedWithLocation()
    {
        using var client = ClientFor(7);

        using var response = await client.PostAsJsonAsync("/orders", new CreateOrder(ProductId: 3, Quantity: 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal(new OrderDto(order!.Id, 3, 2, "Pending"), order);
        Assert.Equal($"/orders/{order.Id}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task CreateOrder_QuantityZero_ReturnsValidationProblem()
    {
        using var client = ClientFor(7);

        using var response = await client.PostAsJsonAsync("/orders", new CreateOrder(ProductId: 3, Quantity: 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(nameof(CreateOrder.Quantity), problem!.Errors.Keys);
    }

    [Fact]
    public async Task GetOrder_SomeoneElsesOrder_ReturnsNotFound()
    {
        using var owner = ClientFor(7);
        using var created = await owner.PostAsJsonAsync("/orders", new CreateOrder(3, 1));
        var order = await created.Content.ReadFromJsonAsync<OrderDto>();

        using var other = ClientFor(8);
        using var response = await other.GetAsync($"/orders/{order!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/orders/{order.Id}")).StatusCode);
    }

    [Fact]
    public async Task NoUser_ReturnsUnauthorized()
    {
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EachTest_StartsWithEmptyTables(int ordersToAdd)
    {
        using var client = ClientFor(7);
        for (var i = 0; i < ordersToAdd; i++)
        {
            using var _ = await client.PostAsJsonAsync("/orders", new CreateOrder(3, 1));
        }

        var mine = await client.GetFromJsonAsync<List<OrderDto>>("/orders");

        Assert.Equal(ordersToAdd, mine!.Count);   // without the reset, earlier tests' orders would show up here
    }
}

public sealed class PostgresOrdersApiTests(PostgresOrdersApi api)
    : OrdersApiTests<PostgresOrdersApi>(api), IClassFixture<PostgresOrdersApi>;

public sealed class SqlServerOrdersApiTests(SqlServerOrdersApi api)
    : OrdersApiTests<SqlServerOrdersApi>(api), IClassFixture<SqlServerOrdersApi>;
