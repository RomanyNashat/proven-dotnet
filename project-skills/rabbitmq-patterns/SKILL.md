---
name: rabbitmq-patterns
description: RabbitMQ for .NET (per-project): exchanges/queues/bindings, pub/sub, retries, dead-letter. MassTransit v9 is commercial — read the licence note first.
version: 1.0.0
---

# RabbitMQ Patterns

> **Per-project skill.** Copy it into a repo only when that repo really uses RabbitMQ; for Kafka see
> `kafka-patterns`.
>
> **Licence: MassTransit is commercial from v9 (January 2026).** It's free only under $1M annual revenue,
> and v8's security fixes are promised only through 2026. So:
> - **Default to `RabbitMQ.Client`** (Apache-2.0 / MPL-2.0).
> - For an outbox plus a bus without MassTransit, see the `cap-library` per-project skill (MIT).
> - The MassTransit sections below apply only to a service that already runs v8, pinned
>   `[8.5.0,9.0.0)`, or one with a purchased v9 licence. That's a decision for the team, not a default
>   (`package-policy`).

## When to Use RabbitMQ vs Kafka

| Use RabbitMQ | Use Kafka |
|-------------|-----------|
| Task queues (work distribution) | Event streaming (log of events) |
| Request-response messaging | High-throughput append-only log |
| Routing with complex topology | Event replay and reprocessing |
| Low-latency, message-level ack | Consumer groups with offset tracking |
| Message priority queues | Long-term event retention |
| Smaller teams, simpler ops | Event sourcing, stream processing |

## MassTransit setup (only with v8 pinned or a v9 licence — see the note at the top)

### Registration
```csharp
builder.Services.AddMassTransit(bus =>
{
    // Auto-discover all consumers in the assembly
    bus.AddConsumers(typeof(OrderCreatedConsumer).Assembly);

    // Register specific sagas
    bus.AddSagaStateMachine<OrderSaga, OrderSagaState>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<AppDbContext>();
            r.UsePostgres();
        });

    bus.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]!);
            h.Password(builder.Configuration["RabbitMQ:Password"]!);
        });

        // Global retry policy
        cfg.UseMessageRetry(retry => retry
            .Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

        // Global circuit breaker
        cfg.UseCircuitBreaker(cb =>
        {
            cb.TrackingPeriod = TimeSpan.FromMinutes(1);
            cb.TripThreshold = 15;
            cb.ActiveThreshold = 10;
            cb.ResetInterval = TimeSpan.FromMinutes(5);
        });

        // Auto-configure endpoints from registered consumers
        cfg.ConfigureEndpoints(context);
    });
});
```

### appsettings.json
```json
{
    "RabbitMQ": {
        "Host": "localhost",
        "Username": "guest",
        "Password": "guest",
        "VirtualHost": "/"
    }
}
```

## Message Contracts

```csharp
// Messages are interfaces or records — shared via a Contracts project
// Never share implementation classes across services

namespace Contracts.Orders;

// Commands — imperative, one consumer
public record SubmitOrder(int OrderId, int CustomerId, List<OrderItem> Items);
public record CancelOrder(int OrderId, string Reason);

// Events — past tense, multiple consumers
public record OrderSubmitted(int OrderId, int CustomerId, decimal Total, DateTimeOffset At);
public record OrderCancelled(int OrderId, string Reason, DateTimeOffset At);
public record OrderShipped(int OrderId, DateTimeOffset ShippedAt);

// Common sub-types
public record OrderItem(int ProductId, string ProductName, int Quantity, decimal UnitPrice);
```

**Naming conventions:**
- Commands: imperative verb (`SubmitOrder`, `CancelOrder`, `ProcessPayment`)
- Events: past tense (`OrderSubmitted`, `PaymentProcessed`, `StockReserved`)
- Place in a shared `Contracts` project referenced by both publisher and consumer

## Consumers

### Basic consumer
```csharp
public sealed class OrderSubmittedConsumer(
    IOrderRepository repository,
    ILogger<OrderSubmittedConsumer> logger) : IConsumer<OrderSubmitted>
{
    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        var message = context.Message;

        logger.LogInformation(
            "Processing OrderSubmitted {OrderId} for customer {CustomerId}",
            message.OrderId, message.CustomerId);

        // Business logic
        var order = await repository.GetByIdAsync(message.OrderId, context.CancellationToken);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found, skipping", message.OrderId);
            return;  // message is acknowledged (not redelivered)
        }

        await ProcessOrderAsync(order, context.CancellationToken);
    }
}
```

### Consumer with retry and error handling
```csharp
public sealed class ProcessPaymentConsumer(
    IPaymentGateway paymentGateway,
    ILogger<ProcessPaymentConsumer> logger) : IConsumer<ProcessPayment>
{
    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        try
        {
            var result = await paymentGateway.ChargeAsync(
                context.Message.OrderId,
                context.Message.Amount,
                context.CancellationToken);

            if (result.IsSuccess)
            {
                await context.Publish(new PaymentProcessed(
                    context.Message.OrderId, result.TransactionId, DateTimeOffset.UtcNow));
            }
            else
            {
                await context.Publish(new PaymentFailed(
                    context.Message.OrderId, result.ErrorMessage, DateTimeOffset.UtcNow));
            }
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            // This exception triggers retry (configured at endpoint level)
            logger.LogWarning(ex, "Payment gateway unavailable for order {OrderId}",
                context.Message.OrderId);
            throw;  // MassTransit retries based on configured policy
        }
    }
}
```

### Consumer endpoint configuration (custom retry, concurrency)
```csharp
bus.UsingRabbitMq((context, cfg) =>
{
    cfg.ReceiveEndpoint("process-payment", e =>
    {
        e.ConfigureConsumer<ProcessPaymentConsumer>(context);

        // Endpoint-specific retry (overrides global)
        e.UseMessageRetry(r => r
            .Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));

        // Concurrency limit
        e.PrefetchCount = 16;
        e.ConcurrentMessageLimit = 8;

        // Dead letter after all retries exhausted
        e.ConfigureDeadLetterQueueDeadLetterTransport();
    });
});
```

## Publishing Messages

```csharp
// Inject IPublishEndpoint for events (fan-out to all consumers)
public sealed class OrderService(
    IPublishEndpoint publishEndpoint,
    IOrderRepository repository)
{
    public async Task CreateOrderAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);
        await repository.AddAsync(order, ct);

        // Publish event — all subscribed consumers receive it
        await publishEndpoint.Publish(new OrderSubmitted(
            order.Id, order.CustomerId, order.Total.Amount, DateTimeOffset.UtcNow), ct);
    }
}

// Inject ISendEndpoint for commands (one specific consumer)
public sealed class PaymentInitiator(
    IEndpointNameFormatter formatter,
    IBus bus)
{
    public async Task InitiatePaymentAsync(int orderId, decimal amount, CancellationToken ct)
    {
        var endpoint = await bus.GetSendEndpoint(
            new Uri($"queue:{formatter.Consumer<ProcessPaymentConsumer>()}"));

        await endpoint.Send(new ProcessPayment(orderId, amount), ct);
    }
}
```

## Request-Response

```csharp
// Request/Response contracts
public record CheckInventory(int ProductId, int RequestedQuantity);
public record InventoryResult(int ProductId, bool IsAvailable, int AvailableQuantity);

// Consumer (responder)
public sealed class CheckInventoryConsumer(
    IInventoryRepository inventory) : IConsumer<CheckInventory>
{
    public async Task Consume(ConsumeContext<CheckInventory> context)
    {
        var stock = await inventory.GetStockAsync(
            context.Message.ProductId, context.CancellationToken);

        await context.RespondAsync(new InventoryResult(
            context.Message.ProductId,
            stock >= context.Message.RequestedQuantity,
            stock));
    }
}

// Client (requester)
public sealed class OrderValidator(IRequestClient<CheckInventory> inventoryClient)
{
    public async Task<bool> ValidateStockAsync(int productId, int quantity, CancellationToken ct)
    {
        var response = await inventoryClient.GetResponse<InventoryResult>(
            new CheckInventory(productId, quantity), ct);

        return response.Message.IsAvailable;
    }
}

// Register request client
bus.AddRequestClient<CheckInventory>(
    new Uri("queue:check-inventory"),
    RequestTimeout.After(s: 10));
```

## Saga (Orchestration)

```csharp
// Saga state
public class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = "";
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset? PaymentProcessedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
}

// Saga state machine
public sealed class OrderSaga : MassTransitStateMachine<OrderSagaState>
{
    public State Submitted { get; private set; } = null!;
    public State PaymentProcessed { get; private set; } = null!;
    public State Shipped { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<OrderSubmitted> OrderSubmittedEvent { get; private set; } = null!;
    public Event<PaymentProcessed> PaymentProcessedEvent { get; private set; } = null!;
    public Event<OrderShipped> OrderShippedEvent { get; private set; } = null!;
    public Event<OrderCancelled> OrderCancelledEvent { get; private set; } = null!;

    public OrderSaga()
    {
        InstanceState(x => x.CurrentState);

        // OrderId is the int key; the saga's own CorrelationId is a Guid MassTransit creates (SelectId).
        // Index saga.OrderId — every event looks the saga up by it.
        Event(() => OrderSubmittedEvent, e =>
        {
            e.CorrelateBy((saga, ctx) => saga.OrderId == ctx.Message.OrderId);
            e.SelectId(_ => NewId.NextGuid());
        });
        Event(() => PaymentProcessedEvent, e => e.CorrelateBy((saga, ctx) => saga.OrderId == ctx.Message.OrderId));
        Event(() => OrderShippedEvent, e => e.CorrelateBy((saga, ctx) => saga.OrderId == ctx.Message.OrderId));
        Event(() => OrderCancelledEvent, e => e.CorrelateBy((saga, ctx) => saga.OrderId == ctx.Message.OrderId));

        Initially(
            When(OrderSubmittedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.OrderId = ctx.Message.OrderId;
                    ctx.Saga.CustomerId = ctx.Message.CustomerId;
                    ctx.Saga.Total = ctx.Message.Total;
                })
                .Send(new Uri("queue:process-payment"),
                    ctx => new ProcessPayment(ctx.Saga.OrderId, ctx.Saga.Total))
                .TransitionTo(Submitted));

        During(Submitted,
            When(PaymentProcessedEvent)
                .Then(ctx => ctx.Saga.PaymentProcessedAt = DateTimeOffset.UtcNow)
                .Publish(ctx => new OrderReadyForShipment(ctx.Saga.OrderId))
                .TransitionTo(PaymentProcessed),
            When(OrderCancelledEvent)
                .TransitionTo(Cancelled)
                .Finalize());

        During(PaymentProcessed,
            When(OrderShippedEvent)
                .Then(ctx => ctx.Saga.ShippedAt = ctx.Message.ShippedAt)
                .TransitionTo(Shipped)
                .Finalize());
    }
}
```

## Dead Letter / Error Handling

```csharp
// MassTransit auto-creates _error and _skipped queues
// Messages that fail all retries go to <queue-name>_error
// Messages that can't be deserialized go to <queue-name>_skipped

// Custom dead letter consumer (process failed messages)
public sealed class OrderSubmittedFaultConsumer
    : IConsumer<Fault<OrderSubmitted>>
{
    public async Task Consume(ConsumeContext<Fault<OrderSubmitted>> context)
    {
        var faultMessage = context.Message.Message;
        var exceptions = context.Message.Exceptions;

        // Log, alert, or compensate
        logger.LogError("OrderSubmitted permanently failed for {OrderId}: {Errors}",
            faultMessage.OrderId,
            string.Join("; ", exceptions.Select(e => e.Message)));

        // Optionally: send to a monitoring queue, create an incident, etc.
    }
}
```

## Testing with Testcontainers

```csharp
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync();
}

// In-memory test harness (no RabbitMQ needed)
[Fact]
public async Task OrderSubmitted_IsConsumed()
{
    await using var provider = new ServiceCollection()
        .AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<OrderSubmittedConsumer>();
        })
        .BuildServiceProvider(true);

    var harness = provider.GetRequiredService<ITestHarness>();
    await harness.Start();

    await harness.Bus.Publish(new OrderSubmitted(
        1001, 42, 100m, DateTimeOffset.UtcNow));

    (await harness.Consumed.Any<OrderSubmitted>()).Should().BeTrue();
    var consumerHarness = harness.GetConsumerHarness<OrderSubmittedConsumer>();
    (await consumerHarness.Consumed.Any<OrderSubmitted>()).Should().BeTrue();
}
```

## Aspire Integration

```csharp
// In AppHost
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

builder.AddProject<Projects.OrderService>("order-service")
    .WithReference(rabbitmq);

// In service
builder.AddMassTransit(/* ... */);
// Connection string auto-injected by Aspire
```

## See also — reliable publishing (outbox)
For "save data AND publish" flows, don't dual-write directly to RabbitMQ — use the **transactional
outbox** (`outbox` skill): event row committed in the same DB transaction as the data, relayed by a
background worker. At-least-once → idempotent consumers. Turnkey option: DotNetCore.CAP (the `cap-library` per-project skill).
