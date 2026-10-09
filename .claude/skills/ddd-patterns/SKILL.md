---
name: ddd-patterns
description: DDD — aggregates loaded whole, int ids from HiLo, value objects as records mapped to bounded columns, domain events in process after SaveChanges and across services through the outbox, repositories per aggregate. Core code tested in CI on PostgreSQL and SQL Server.
version: 2.1.0
---

# DDD Patterns

The model knows DDD. This skill holds what's specific here, and the places where the common example
breaks these rules or your data. The code marked as a sample is tested in CI against **both** PostgreSQL 17
and SQL Server 2022 (`tests/SkillSamples.Tests/Ddd`): the same tests run on each engine.

Related: `design-patterns` (§4: repository and Unit of Work verdicts), `efcore-patterns` (mapping, column
rules), `outbox` (events that leave the service), `cqrs-eventsourcing` (handlers and the in-process event
dispatcher), `rules/architecture.md` (the layers).

## 1. An aggregate

<!-- sample: tests/SkillSamples.Tests/Ddd/Order.cs -->
```csharp
public sealed class OrderLine
{
    private OrderLine()
    {
    }

    internal OrderLine(int productId, string productName, int quantity, Money unitPrice)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = "";
    public int Quantity { get; private set; }
    public Money UnitPrice { get; private set; } = Money.Zero("SAR");
    public Money Subtotal => UnitPrice.Times(Quantity);

    internal void Increase(int quantity) => Quantity += quantity;
}

/// <summary>
/// The aggregate owns its lines: they change only through its methods, and Total is derived from them.
/// That only holds when the whole aggregate is loaded (see OrderRepository).
/// </summary>
public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public bool IsCancelled { get; private set; }
    public Money Total { get; private set; } = Money.Zero("SAR");
    public IReadOnlyList<OrderLine> Lines => _lines;

    public static Order Place(int customerId, string currency) =>
        new() { CustomerId = customerId, Total = Money.Zero(currency) };

    public void AddLine(int productId, string productName, int quantity, Money unitPrice)
    {
        if (IsCancelled)
        {
            throw new InvalidOperationException("A cancelled order can't change");
        }

        var existing = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (existing is null)
        {
            _lines.Add(new OrderLine(productId, productName, quantity, unitPrice));
        }
        else
        {
            existing.Increase(quantity);
        }

        Total = _lines.Aggregate(Money.Zero(Total.Currency), (sum, line) => sum.Add(line.Subtotal));
    }

    public void Cancel() => IsCancelled = true;
}
```

- **State changes through methods only**; collections are exposed read-only over a private field.
- **Derived values (Total) are computed from the children**, so they're only right when the children
  are loaded: §2.
- **One aggregate per transaction.** Reference other aggregates by id, never by navigation.
- A broken rule is a domain exception (`rules/architecture.md`); an expected outcome the caller must
  handle (slot taken, not allowed) goes back to the handler as a Result.

## 2. Load the whole aggregate

<!-- sample: tests/SkillSamples.Tests/Ddd/OrderRepository.cs -->
```csharp
/// <summary>
/// Loads and adds whole aggregates, nothing else. No Update() (the context tracks the changes), no
/// partial loads, no IQueryable out of the repository. Saving is the caller's unit of work.
/// </summary>
public sealed class OrderRepository(OrdersDbContext db)
{
    public Task<Order?> GetAsync(int id, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddAsync(Order order, CancellationToken ct) =>
        await db.Orders.AddAsync(order, ct);   // AddAsync: HiLo may fetch the next block of ids here
}
```

Tested: an order loaded with `FindAsync` (no lines) and given a new line saves a Total that counts only
the new line, while all the lines are in the table. Nothing fails; the number is just wrong. Loaded
through the repository, the Total covers every line.
- A repository method returns a whole aggregate or nothing. Reads that need part of it are queries
  (Dapper or a projection), not aggregates.
- No `Update(order)`: the context already tracks the loaded aggregate, and `Update` marks every column
  of the whole graph as modified.
- In Clean Architecture, Application saves through a thin `IUnitOfWork` port (`design-patterns` §4).

## 3. Ids

Keys are `int` (column rules). An aggregate whose events carry its id needs the id before `SaveChanges`:
`UseHiLo()` gives it at `AddAsync` (tested above: the id is set before the save). Raise the event as a
factory that runs when the events are collected, so it reads the id assigned in between: the `outbox`
sample (`tests/SkillSamples.Tests/EfOutbox/Aggregates.cs`) does exactly this, tested. Don't add a
separate id-generator service, and don't add a GUID "public id" beside the int.

## 4. Value objects and their mapping

<!-- sample: tests/SkillSamples.Tests/Ddd/Money.cs -->
```csharp
/// <summary>A value object: equal by value, immutable, and it refuses to add two currencies.</summary>
public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other) =>
        other.Currency == Currency
            ? this with { Amount = Amount + other.Amount }
            : throw new InvalidOperationException($"Can't add {other.Currency} to {Currency}");

    public Money Times(int quantity) => this with { Amount = Amount * quantity };
}
```

<!-- sample: tests/SkillSamples.Tests/Ddd/OrdersDbContext.cs -->
```csharp
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var sqlServer = Database.IsSqlServer();
        modelBuilder.ApplyConfiguration(new OrderConfiguration(sqlServer));
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration(sqlServer));
    }
}

/// <summary>A service has one engine and keeps one branch; both are here so CI checks both.</summary>
public sealed class OrderConfiguration(bool sqlServer) : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        // An int, known at AddAsync, before SaveChanges. With one provider this is .UseHiLo("orders_hilo");
        // the samples project references both, so each call names its provider.
        var id = builder.Property(o => o.Id);
        if (sqlServer)
        {
            SqlServerPropertyBuilderExtensions.UseHiLo(id, "orders_hilo");
        }
        else
        {
            NpgsqlPropertyBuilderExtensions.UseHiLo(id, "orders_hilo");
        }

        builder.ComplexProperty(o => o.Total, money =>
        {
            money.Property(m => m.Amount).HasColumnName("total_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("total_currency").HasMaxLength(3).IsUnicode(false);
        });
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(o => o.CustomerId);
    }
}

public sealed class OrderLineConfiguration(bool sqlServer) : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");
        if (sqlServer)
        {
            builder.Property(l => l.Id).UseIdentityColumn();
        }
        else
        {
            builder.Property(l => l.Id).UseIdentityAlwaysColumn();
        }

        builder.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
        builder.ComplexProperty(l => l.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("unit_price_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("unit_price_currency").HasMaxLength(3).IsUnicode(false);
        });
        builder.Ignore(l => l.Subtotal);
    }
}
```

- Value objects are `sealed record`s, mapped with `ComplexProperty` (EF 8+). Every string column in them
  gets `HasMaxLength` like any other (`rules/efcore-rules.md`); money is `decimal` with precision, never a
  `money` column.
- Entities keep reference equality. Don't give an entity base class `Equals` on `Id`: two new entities
  both have id 0 until saved, so they compare equal and one disappears from a `HashSet`.

## 5. Domain events

- **In process** (another aggregate in this service, a read model): dispatched after `SaveChangesAsync`
  returns, through the `DomainEventDispatcher` in `cqrs-eventsourcing`. No mediator library.
- **Across services** (Kafka): through the **outbox**, written in the same transaction (`outbox`). A
  handler that calls a Kafka producer is a dual write: a crash after the commit loses the event.
- `OccurredAt` comes from the injected `TimeProvider` (the outbox interceptor stamps it), never
  `DateTimeOffset.UtcNow` in an event base class.
- Events carry ids and facts, not patient details: consumers fetch what they're allowed to see.

## 6. Bounded contexts

- Each context owns its database; no shared tables.
- Between contexts: integration events (Kafka, through the outbox) or an API call, never the other
  context's tables.
- The same word can mean different models (a Product in Orders isn't the Product in Inventory);
  translate at the boundary instead of sharing the class.

## 7. Review checklist
- An aggregate method called on an aggregate loaded without its children (`FindAsync`, no `Include`).
- A repository returning `IQueryable`, part of an aggregate, or calling `Update` on tracked entities.
- An entity base class with `Equals`/`GetHashCode` on `Id`.
- An id generator service or a GUID public id beside an `int` key; an event raised with id 0.
- A domain-event handler that publishes to Kafka directly.
- `DateTime.UtcNow`/`DateTimeOffset.UtcNow` in an event or entity; a value-object string without a length.
