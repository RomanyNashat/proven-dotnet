---
name: cqrs-eventsourcing
description: CQRS without a mediator library: command/query split, plain handlers + decorators (validation, transaction, logging), EF write + Dapper read, domain events, Marten event sourcing. MediatR only by name.
version: 1.0.0
---

# CQRS & Event Sourcing Patterns

## CQRS Architecture

```
Commands (Write Side)                    Queries (Read Side)
─────────────────────                    ────────────────────
CreateOrderCommand                       GetOrderByIdQuery
  → LoggingCommandDecorator                → LoggingQueryDecorator
  → ValidationDecorator                    → Dapper raw SQL
  → TransactionDecorator                   → No change tracking
  → CreateOrderHandler                     → AsNoTracking (if EF)
    → Domain aggregate                     → Cached when appropriate
    → EF Core SaveChanges
    → Domain events dispatched
```

## No mediator library

proven-dotnet doesn't use MediatR by default. From 13.0 it is commercial, the last free version (12.5.0) gets no
security fixes, and the team doesn't want it (see `package-policy`). The pattern doesn't need a library:
a command or query is a record, a handler is a class, decorators do what pipeline behaviours did, and
an endpoint asks DI for the handler it needs.

**If the developer asks for MediatR by name:** pin `[12.5.0,13.0.0)`, say once that it is past its
security support and commercial above that version, and follow the codebase if it already uses it.

All code in this skill marked as a sample is compiled and tested in CI (`tests/SkillSamples.Tests/Cqrs/`).

## Contracts

<!-- sample: tests/SkillSamples.Tests/Cqrs/Contracts.cs -->
```csharp
// Application layer. No library: a command or query is a record, a handler is a class, and the endpoint
// asks DI for the handler it needs.
public interface ICommand<TResult>;
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}
```

## Commands (Write Side)

### Command definition
```csharp
public sealed record CreateOrderCommand(
    List<OrderItemRequest> Items,
    string? Notes) : ICommand<OrderDto>;

public sealed record OrderItemRequest(
    int ProductId,
    int Quantity);
```

### Command handler
```csharp
public sealed class CreateOrderHandler(
    IOrderRepository orderRepository,
    AppDbContext db,
    OrderPricingService pricingService,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : ICommandHandler<CreateOrderCommand, OrderDto>
{
    public async Task<OrderDto> HandleAsync(
        CreateOrderCommand command, CancellationToken ct)
    {
        var lineItems = await pricingService.CreateLineItemsAsync(command.Items, ct);

        var order = Order.Create(
            currentUser.UserId,
            lineItems,
            timeProvider);

        await orderRepository.AddAsync(order, ct);
        await db.SaveChangesAsync(ct);  // inside the TransactionDecorator; domain events dispatched after it

        return order.ToDto();
    }
}
```

### Command validator
```csharp
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("Order must have at least one item");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty()
                .WithMessage("Product ID is required");

            item.RuleFor(i => i.Quantity)
                .InclusiveBetween(1, 1000)
                .WithMessage("Quantity must be between 1 and 1000");
        });

        RuleFor(x => x.Notes)
            .MaximumLength(500)
            .When(x => x.Notes is not null);
    }
}
```

## Queries (Read Side)

### Query definition
```csharp
public sealed record GetOrderByIdQuery(int OrderId) : IQuery<OrderDto?>;

public sealed record GetOrdersQuery(
    OrderStatus? Status,
    DateTimeOffset? FromDate,
    int Page,
    int PageSize) : IQuery<PagedResult<OrderSummaryDto>>;
```

### Query handler (Dapper — raw SQL for performance)
```csharp
public sealed class GetOrderByIdHandler(NpgsqlDataSource dataSource)
    : IQueryHandler<GetOrderByIdQuery, OrderDto?>
{
    public async Task<OrderDto?> HandleAsync(
        GetOrderByIdQuery query, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var orderDict = new Dictionary<Guid, OrderDto>();

        await conn.QueryAsync<OrderDto, LineItemDto, OrderDto>(
            """
            SELECT o.id, o.customer_id, o.total_amount, o.total_currency,
                   o.status, o.created_at, o.shipped_at,
                   li.id, li.product_id, li.product_name,
                   li.quantity, li.unit_price_amount
            FROM orders o
            LEFT JOIN order_line_items li ON li.order_id = o.id
            WHERE o.id = @OrderId
            """,
            (order, lineItem) =>
            {
                if (!orderDict.TryGetValue(order.Id, out var existing))
                {
                    existing = order;
                    existing.Items = [];
                    orderDict[order.Id] = existing;
                }
                if (lineItem is not null)
                    existing.Items.Add(lineItem);
                return existing;
            },
            new { query.OrderId },
            splitOn: "id");

        return orderDict.Values.FirstOrDefault();
    }
}
```

### Paginated query handler
```csharp
public sealed class GetOrdersHandler(NpgsqlDataSource dataSource)
    : IQueryHandler<GetOrdersQuery, PagedResult<OrderSummaryDto>>
{
    public async Task<PagedResult<OrderSummaryDto>> HandleAsync(
        GetOrdersQuery query, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var builder = new SqlBuilder();
        var countTemplate = builder.AddTemplate(
            "SELECT COUNT(*) FROM orders o /**where**/");
        var dataTemplate = builder.AddTemplate("""
            SELECT o.id, o.total_amount AS total, o.status, o.created_at,
                   COUNT(li.id) AS item_count
            FROM orders o
            LEFT JOIN order_line_items li ON li.order_id = o.id
            /**where**/
            GROUP BY o.id, o.total_amount, o.status, o.created_at
            /**orderby**/
            LIMIT @PageSize OFFSET @Offset
            """, new { PageSize = query.PageSize, Offset = (query.Page - 1) * query.PageSize });

        builder.Where("o.is_deleted = false");

        if (query.Status.HasValue)
            builder.Where("o.status = @Status", new { Status = query.Status.Value.ToString() });
        if (query.FromDate.HasValue)
            builder.Where("o.created_at >= @FromDate", new { FromDate = query.FromDate.Value });

        builder.OrderBy("o.created_at DESC");

        var totalCount = await conn.ExecuteScalarAsync<int>(
            countTemplate.RawSql, countTemplate.Parameters);
        var items = (await conn.QueryAsync<OrderSummaryDto>(
            dataTemplate.RawSql, dataTemplate.Parameters)).AsList();

        return new PagedResult<OrderSummaryDto>(items, totalCount, query.Page, query.PageSize);
    }
}
```

## Decorators (what pipeline behaviours were)

Order for commands: **logging → validation → transaction → handler**. Queries get logging only.

<!-- sample: tests/SkillSamples.Tests/Cqrs/Decorators.cs -->
```csharp
public sealed class LoggingCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner, ILogger<LoggingCommandDecorator<TCommand, TResult>> logger)
    : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.HandleAsync(command, ct);
        var elapsed = Stopwatch.GetElapsedTime(started);
        // The name and the time only: a command carries user input, which can be patient data.
        logger.Log(elapsed.TotalMilliseconds > 500 ? LogLevel.Warning : LogLevel.Information,
            "Handled {Command} in {ElapsedMs:F0} ms", typeof(TCommand).Name, elapsed.TotalMilliseconds);
        return result;
    }
}
```
Log the command's **name and the time**, never the command itself (`{@Command}`): commands carry user
input, which can be patient data.

<!-- sample: tests/SkillSamples.Tests/Cqrs/Decorators.cs -->
```csharp
public sealed class ValidationDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner, IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            failures.AddRange((await validator.ValidateAsync(command, ct)).Errors);
        }

        if (failures.Count > 0) throw new ValidationException(failures);   // → 400 ProblemDetails
        return await inner.HandleAsync(command, ct);
    }
}
```

<!-- sample: tests/SkillSamples.Tests/Cqrs/Decorators.cs -->
```csharp
public sealed class TransactionDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner, IUnitOfWork unitOfWork)
    : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public Task<TResult> HandleAsync(TCommand command, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(token => inner.HandleAsync(command, token), ct);
}
```

The EF Core `IUnitOfWork`, using the execution strategy so retries and the transaction work together:
```csharp
public sealed class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await work(ct);   // already inside one

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var result = await work(token);
            await transaction.CommitAsync(token);
            return result;
        }, ct);
    }
}
```

### Registration

<!-- sample: tests/SkillSamples.Tests/Cqrs/CqrsRegistration.cs -->
```csharp
    public static IServiceCollection AddCqrsHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });
        foreach (var type in handlerTypes)
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();
                var args = contract.GetGenericArguments();
                if (definition == typeof(ICommandHandler<,>))
                {
                    services.AddScoped(type);
                    services.AddScoped(contract, sp => Wrap(sp, sp.GetRequiredService(type), args,
                        typeof(TransactionDecorator<,>), typeof(ValidationDecorator<,>), typeof(LoggingCommandDecorator<,>)));
                }
                else if (definition == typeof(IQueryHandler<,>))
                {
                    services.AddScoped(type);
                    services.AddScoped(contract, sp => Wrap(sp, sp.GetRequiredService(type), args,
                        typeof(LoggingQueryDecorator<,>)));
                }
            }
        }

        return services;
    }

    // Innermost first: each decorator receives the one built before it.
    private static object Wrap(IServiceProvider sp, object handler, Type[] args, params Type[] decorators) =>
        decorators.Aggregate(handler, (inner, decorator) =>
            ActivatorUtilities.CreateInstance(sp, decorator.MakeGenericType(args), inner));
```
```csharp
// Program.cs / Application DependencyInjection.cs
services.AddScoped<IUnitOfWork, EfUnitOfWork>();
services.AddValidatorsFromAssembly(typeof(CreateOrderCommandValidator).Assembly);
services.AddCqrsHandlers(typeof(CreateOrderHandler).Assembly);
```
Tested: the command runs inside the transaction after validation; an invalid command stops before
the transaction; a query gets logging only; the outermost decorator is logging.

### Endpoints call the handler they need
```csharp
group.MapPost("/", async Task<Created<OrderDto>> (
    CreateOrderRequest request, ICommandHandler<CreateOrderCommand, OrderDto> handler, CancellationToken ct) =>
{
    var order = await handler.HandleAsync(request.ToCommand(), ct);
    return TypedResults.Created($"/orders/{order.Id}", order);
});
```
The handler a route uses is visible in its signature: no dispatcher to step through, and "who calls
this handler" is a plain `find_callers`.

## Domain events

Dispatched after `SaveChanges` succeeds (see `ddd-patterns`); every handler for the event runs:

<!-- sample: tests/SkillSamples.Tests/Cqrs/DomainEvents.cs -->
```csharp
public interface IDomainEvent;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

/// <summary>Calls every handler registered for an event. Run after SaveChanges (see ddd-patterns).</summary>
public sealed class DomainEventDispatcher(IServiceProvider services)
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in services.GetServices(handlerType))
            {
                await (Task)handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!
                    .Invoke(handler, [domainEvent, ct])!;
            }
        }
    }
}
```

## Paged Result

```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
```

## Event Sourcing with Marten 7.x (optional)

### Setup
```csharp
builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);
    options.DatabaseSchemaName = "orders";

    // Projections — build read models from events
    options.Projections.Add<OrderSummaryProjection>(ProjectionLifecycle.Inline);
    options.Projections.Add<CustomerOrderStatsProjection>(ProjectionLifecycle.Async);
})
.UseLightweightSessions()
.OptimizeArtifactWorkflow();
```

### Event-sourced aggregate
```csharp
// Events as records
public sealed record OrderPlaced(Guid OrderId, int CustomerId, decimal Total, DateTimeOffset At);
public sealed record OrderItemAdded(int ProductId, string ProductName, int Quantity, decimal UnitPrice);
public sealed record OrderShipped(DateTimeOffset ShippedAt);
public sealed record OrderCancelled(string Reason, DateTimeOffset At);

// Aggregate applies events to build state
public class OrderAggregate
{
    public Guid Id { get; private set; }
    public int CustomerId { get; private set; }
    public decimal Total { get; private set; }
    public string Status { get; private set; } = "Draft";
    public List<OrderLineItem> Items { get; private set; } = [];

    // Marten calls Apply() to replay events
    public void Apply(OrderPlaced e)
    {
        Id = e.OrderId;
        CustomerId = e.CustomerId;
        Total = e.Total;
        Status = "Pending";
    }

    public void Apply(OrderItemAdded e)
    {
        Items.Add(new OrderLineItem(e.ProductId, e.ProductName, e.Quantity, e.UnitPrice));
    }

    public void Apply(OrderShipped e) => Status = "Shipped";
    public void Apply(OrderCancelled e) => Status = "Cancelled";
}
```

### Appending events and loading aggregate
```csharp
public sealed class OrderEventHandler(IDocumentSession session)
{
    public async Task CreateOrderAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        // Marten stream IDs are created by the app, not a table identity, so a GUID is right here —
        // the one standing exception to the int-key rule (event streams).
        var orderId = Guid.CreateVersion7();

        session.Events.StartStream<OrderAggregate>(orderId,
            new OrderPlaced(orderId, cmd.CustomerId, cmd.Total, DateTimeOffset.UtcNow));

        foreach (var item in cmd.Items)
        {
            session.Events.Append(orderId,
                new OrderItemAdded(item.ProductId, item.Name, item.Quantity, item.UnitPrice));
        }

        await session.SaveChangesAsync(ct);
    }

    public async Task<OrderAggregate?> GetOrderAsync(Guid orderId, CancellationToken ct)
    {
        // Replays all events to build current state
        return await session.Events.AggregateStreamAsync<OrderAggregate>(orderId, token: ct);
    }
}
```

### Inline projection (read model)
```csharp
public class OrderSummaryProjection : SingleStreamProjection<OrderSummaryReadModel>
{
    public OrderSummaryReadModel Create(OrderPlaced e) => new()
    {
        Id = e.OrderId,
        CustomerId = e.CustomerId,
        Total = e.Total,
        Status = "Pending",
        CreatedAt = e.At
    };

    public void Apply(OrderShipped e, OrderSummaryReadModel model)
    {
        model.Status = "Shipped";
        model.ShippedAt = e.ShippedAt;
    }

    public void Apply(OrderCancelled e, OrderSummaryReadModel model)
    {
        model.Status = "Cancelled";
    }
}

public class OrderSummaryReadModel
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
}
```

## When to Use Event Sourcing

| Use Event Sourcing When | Use State-Based (EF Core) When |
|------------------------|-------------------------------|
| Full audit trail is legally required | Simple CRUD with minimal history needs |
| Time-travel queries are needed | Read-heavy with simple queries |
| Complex domain with many state transitions | Domain logic is straightforward |
| Event-driven architecture is already in place | Team is new to DDD/ES |
| Compliance requires immutable records | Performance of projections isn't acceptable |

**Default recommendation**: Start with state-based (EF Core) + domain events. Adopt event sourcing per-aggregate when the audit/history requirement justifies the complexity.
