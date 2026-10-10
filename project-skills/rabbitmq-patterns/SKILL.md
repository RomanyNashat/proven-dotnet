---
name: rabbitmq-patterns
description: RabbitMQ for .NET with RabbitMQ.Client 7 (per-project) — quorum queues with a delivery limit and a dead-letter queue, publisher confirms, manual acks, competing consumers, idempotent handlers. MassTransit v9 is commercial; read the licence note. Tested in CI against RabbitMQ 4.1.
version: 2.0.0
---

# RabbitMQ Patterns

> **Per-project skill.** Copy it into a repo only when that repo really uses RabbitMQ; for Kafka see
> `kafka-patterns`.
>
> **Licence: MassTransit is commercial from v9 (January 2026).** It's free only under $1M annual revenue,
> and v8's security fixes are promised only through 2026. So:
> - **Default to `RabbitMQ.Client`** (Apache-2.0 / MPL-2.0). Everything below uses it, and it's tested.
> - For an outbox plus a bus without MassTransit, see the `cap-library` per-project skill (MIT).
> - MassTransit only in a service that already runs v8, pinned `[8.5.0,9.0.0)`, or with a purchased v9
>   licence. That's a decision for the team, not a default.

## When to use RabbitMQ vs Kafka

| Use RabbitMQ | Use Kafka |
|-------------|-----------|
| Task queues (work distribution) | Event streaming (a log of events) |
| Routing with exchanges and bindings | High-throughput append-only log |
| Per-message acknowledgement | Replay and reprocessing |
| Priority queues, per-message TTL | Long retention, consumer groups with offsets |

## Topology: a quorum queue with a dead-letter queue

<!-- sample: tests/SkillSamples.Tests/RabbitMq/OrderTopology.cs -->
```csharp
// Declared by the service at start-up (declaring what already exists with the same arguments is a no-op).
public sealed record OrderTopology(string Exchange, string Queue)
{
    public string DeadLetterExchange => $"{Exchange}.dead";
    public string DeadLetterQueue => $"{Queue}.dead";

    public async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);

        // A quorum queue (replicated) that gives up on a message after five failed deliveries and moves it
        // to the dead-letter queue. Without a dead-letter exchange it would drop it.
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = 5,
                ["x-dead-letter-exchange"] = DeadLetterExchange
            }, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, "order.created", cancellationToken: ct);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, "", cancellationToken: ct);
    }
}
```

- **Quorum queues**, not classic: replicated, and they count deliveries. Since RabbitMQ 4.0 a quorum
  queue has a delivery limit of 20 by default, and **drops** a message that reaches it unless the queue
  has a dead-letter exchange (tested as a story: a message that always fails is gone after 20 tries).
  Set both, as above.
- One queue per consuming service (`billing.order-created`), bound to the exchange by routing key.
  Publishers know the exchange, never the queues.

## Publishing: confirmed, persistent, mandatory

<!-- sample: tests/SkillSamples.Tests/RabbitMq/OrderPublisher.cs -->
```csharp
public sealed record OrderCreated(int OrderId, decimal Total);

public sealed class OrderPublisher(IChannel channel, OrderTopology topology)
{
    // Open the channel with publisher confirmations and tracking:
    //   connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true))
    // Then PublishAsync returns once the broker has stored the message, and throws PublishException if it
    // refused it or (mandatory) no queue was bound to take it.
    public async Task PublishAsync(OrderCreated order, CancellationToken ct)
    {
        var properties = new BasicProperties
        {
            Persistent = true,                                   // written to disk, survives a broker restart
            ContentType = "application/json",
            MessageId = $"order-created-{order.OrderId}",        // consumers deduplicate on it
            Type = nameof(OrderCreated)
        };

        await channel.BasicPublishAsync(topology.Exchange, "order.created", mandatory: true, properties,
            JsonSerializer.SerializeToUtf8Bytes(order), ct);
    }
}
```

Tested as stories:
- **The publisher waits for the broker:** when `PublishAsync` returns, the order is in the queue.
- **No queue is bound for the event:** with `mandatory: true` the publisher gets `PublishException`
  (`IsReturn`). Without it, the broker drops the message and says nothing.

Publishing straight after a database commit is a dual write: if the process dies in between, the event
is lost. Publish from an outbox (`outbox`, or `cap-library`).

## Consuming: manual acks, failures to the dead-letter queue

<!-- sample: tests/SkillSamples.Tests/RabbitMq/OrderConsumer.cs -->
```csharp
public interface IOrderHandler
{
    Task HandleAsync(OrderCreated order, string messageId, CancellationToken ct);
}

public static class OrderConsumer
{
    // Manual acknowledgement: a message is removed only after it was handled. If the process dies first,
    // the broker delivers it again, so the handler must be idempotent (deduplicate on MessageId).
    public static async Task<string> StartAsync(
        IChannel channel, OrderTopology topology, IOrderHandler handler, ILogger logger, CancellationToken ct)
    {
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 16, global: false, ct);   // at most 16 unacked at once

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var order = JsonSerializer.Deserialize<OrderCreated>(delivery.Body.Span)
                    ?? throw new JsonException("Empty message.");
                await handler.HandleAsync(order, delivery.BasicProperties.MessageId ?? "", ct);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Message {MessageId} failed; back to the queue", delivery.BasicProperties.MessageId);
                // Back to the queue. The delivery limit stops a message that always fails from looping
                // forever: after five tries it moves to the dead-letter queue for a person to look at.
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
            }
        };

        return await channel.BasicConsumeAsync(topology.Queue, autoAck: false, consumer, ct);
    }
}
```

Tested as stories:
- **A consumer dies before acking:** the next one gets the same message, marked `Redelivered`, with the
  same `MessageId`. That's why handlers deduplicate.
- **An order always fails:** after its tries it waits in the dead-letter queue and the main queue is
  empty, instead of looping forever.
- **Two instances consume the queue:** 40 orders, each handled once, spread over both.

Tested with no ICU: an order's amount round-trips exactly.

- **One connection per process; a channel per consumer, and its own for publishing.** A connection is
  expensive (TCP, TLS, a server process); channels are cheap.
- **`prefetchCount`** bounds the unacked messages per consumer: the work in flight, and what's redelivered
  when the pod dies.
- **Retry with a delay** (a payment gateway that's down for a minute): requeue retries at once. For a
  delay, dead-letter into a wait queue with a message TTL that dead-letters back to the main exchange.

## Request/response

Prefer HTTP or gRPC for questions that need an answer. RabbitMQ can do RPC (a `ReplyTo` queue and a
`CorrelationId`), but timeouts, lost replies and scaling responders all become your code.

## MassTransit (only with v8 pinned or a v9 licence)

MassTransit adds consumers by type, retries, sagas and an in-memory test harness on top of RabbitMQ.
None of its code here is tested. In a service that already runs it:
- Pin `[8.5.0,9.0.0)` and plan the move: v8's security fixes are promised only through 2026.
- Failed messages go to `<queue>_error`, unreadable ones to `<queue>_skipped`; watch both.
- Its retry happens inside the consumer before the message is nacked; don't stack a broker delivery
  limit that's lower than the retry count.

## Rules
- `RabbitMQ.Client` by default; MassTransit only by a recorded team decision.
- Quorum queues with a delivery limit and a dead-letter exchange.
- Publisher confirms, `Persistent`, `mandatory`, a `MessageId`; publish through an outbox when it follows a commit.
- Manual acks after the work; idempotent handlers.
- A prefetch limit per consumer.
