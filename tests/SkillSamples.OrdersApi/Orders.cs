using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SkillSamples.OrdersApi;

public sealed class Order
{
    public Order(int ownerId, int productId, int quantity)
    {
        OwnerId = ownerId;
        ProductId = productId;
        Quantity = quantity;
    }

    public int Id { get; private set; }

    public int OwnerId { get; private set; }

    public int ProductId { get; private set; }

    public int Quantity { get; private set; }

    public string Status { get; private set; } = "Pending";
}

public sealed record CreateOrder(int ProductId, int Quantity);

public sealed record OrderDto(int Id, int ProductId, int Quantity, string Status);

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new OrderConfiguration(Database.IsSqlServer()));
}

public sealed class OrderConfiguration(bool sqlServer) : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        var id = builder.Property(o => o.Id).HasColumnName("id");
        if (sqlServer)
        {
            id.UseIdentityColumn();
        }
        else
        {
            id.UseIdentityAlwaysColumn();
        }

        builder.Property(o => o.OwnerId).HasColumnName("owner_id");
        builder.Property(o => o.ProductId).HasColumnName("product_id");
        builder.Property(o => o.Quantity).HasColumnName("quantity");
        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(o => new { o.OwnerId, o.Id }).HasDatabaseName("ix_orders_owner_id");
    }
}

public static class OrdersDb
{
    /// <summary>
    /// Reads the connection string when the context is built, not at startup, so a test can supply it
    /// with UseSetting and replace nothing. A service has one engine; this one runs on both for CI.
    /// </summary>
    public static IServiceCollection AddOrdersDb(this IServiceCollection services) =>
        services.AddDbContext<OrdersDbContext>((sp, options) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config.GetConnectionString("Orders")
                ?? throw new InvalidOperationException("ConnectionStrings:Orders is not set.");
            if (config["Database:Engine"] == "SqlServer")
            {
                options.UseSqlServer(connectionString);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
        });
}

public static class OrderEndpoints
{
    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").RequireAuthorization();
        orders.MapPost("/", Create);
        orders.MapGet("/{id:int}", GetById);
        orders.MapGet("/", ListMine);
    }

    private static int CallerId(ClaimsPrincipal user) => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<Results<Created<OrderDto>, ValidationProblem>> Create(
        CreateOrder request, ClaimsPrincipal user, OrdersDbContext db, CancellationToken ct)
    {
        if (request.Quantity is < 1 or > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateOrder.Quantity)] = ["Quantity must be between 1 and 100."],
            });
        }

        var order = new Order(CallerId(user), request.ProductId, request.Quantity);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/orders/{order.Id}", ToDto(order));
    }

    // Someone else's order is NotFound, not Forbid: a 403 tells the caller the id exists (rules/security.md).
    private static async Task<Results<Ok<OrderDto>, NotFound>> GetById(
        int id, ClaimsPrincipal user, OrdersDbContext db, CancellationToken ct)
    {
        var callerId = CallerId(user);
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == id && o.OwnerId == callerId)
            .Select(o => new OrderDto(o.Id, o.ProductId, o.Quantity, o.Status))
            .SingleOrDefaultAsync(ct);
        return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
    }

    private static async Task<Ok<List<OrderDto>>> ListMine(ClaimsPrincipal user, OrdersDbContext db, CancellationToken ct)
    {
        var callerId = CallerId(user);
        return TypedResults.Ok(await db.Orders.AsNoTracking()
            .Where(o => o.OwnerId == callerId)
            .OrderBy(o => o.Id)
            .Select(o => new OrderDto(o.Id, o.ProductId, o.Quantity, o.Status))
            .ToListAsync(ct));
    }

    private static OrderDto ToDto(Order o) => new(o.Id, o.ProductId, o.Quantity, o.Status);
}
