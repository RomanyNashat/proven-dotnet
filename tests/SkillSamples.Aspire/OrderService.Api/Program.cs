using StackExchange.Redis;

// OrderService.Api/Program.cs: the connection string for "redis" comes from the AppHost's WithReference.
var builder = WebApplication.CreateBuilder(args);
builder.AddRedisClient("redis");

var app = builder.Build();
app.MapGet("/health/live", () => "Healthy");
app.MapPost("/api/orders", async (OrderRequest order, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var id = await db.StringIncrementAsync("orders:next-id");
    await db.StringSetAsync($"orders:{id}", order.ProductId);
    return Results.Created($"/api/orders/{id}", new { id });
});
app.MapGet("/api/orders/{id:long}", async (long id, IConnectionMultiplexer redis) =>
{
    var productId = await redis.GetDatabase().StringGetAsync($"orders:{id}");
    return productId.IsNull ? Results.NotFound() : Results.Ok(new { id, productId = (int)productId });
});
app.Run();

internal sealed record OrderRequest(int ProductId, int Quantity);
