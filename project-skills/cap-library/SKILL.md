---
name: cap-library
description: DotNetCore.CAP for .NET: outbox + event bus over Kafka/RabbitMQ, SqlServer/PostgreSQL/Mongo. At-least-once → idempotent consumers.
---

# DotNetCore.CAP — outbox + event bus, batteries included

`DotNetCore.CAP` is a .NET library that gives you the **transactional outbox pattern and a
publish/subscribe event bus in one package**. Where the `outbox` skill is the *pattern* you build by
hand, CAP is the *turnkey library* that implements it — you get the outbox table, the relay, retries,
and a pub/sub API without wiring them yourself.

## What it does
- **Transactional messaging** — publish inside your DB transaction; CAP stores the message in its
  outbox tables (`cap.Published` / `cap.Received`) atomically with your data, then relays it to the
  broker. Same dual-write guarantee as a hand-rolled outbox.
- **Pub/sub** — `[CapSubscribe("event.name")]` on a handler method; `ICapPublisher.PublishAsync(...)`
  to send. CAP routes through the broker and delivers to subscribers.
- **Retries + dead-letter** — failed sends/receives are retried on a schedule; persistent failures are
  parked.

## Wiring
```csharp
builder.Services.AddCap(x =>
{
    x.UseEntityFramework<AppDbContext>();     // storage: EF (SqlServer/PostgreSQL) or Mongo
    x.UseKafka("broker:9092");                // transport: Kafka or RabbitMQ
    // x.UseRabbitMQ("host");
    x.FailedRetryCount = 5;
});
```
- **Transports:** Kafka, RabbitMQ (pick what your platform provides).
- **Storage:** SQL Server, PostgreSQL, MongoDB (CAP creates its own tables).

## Publish inside a transaction (the point)
```csharp
using var tx = dbContext.Database.BeginTransaction(capPublisher, autoCommit: false);
dbContext.Orders.Add(order);
await dbContext.SaveChangesAsync();
await capPublisher.PublishAsync("order.created", new OrderCreated(order.Id));
await tx.CommitAsync();   // data + message commit together
```

## Subscribe
```csharp
public class OrderConsumer : ICapSubscribe
{
    [CapSubscribe("order.created")]
    public async Task Handle(OrderCreated e) { /* ... */ }
}
```

## The honest constraint
CAP is **at-least-once**, not exactly-once. A message can be delivered more than once (relay retry
after a crash). **Consumers must be idempotent** — dedup on the message id or make the handler
naturally idempotent. This is the same rule as the raw outbox; CAP doesn't remove it.

## CAP vs hand-rolled outbox
- Reach for **CAP** when you want the outbox + pub/sub working fast with retries and dead-letter
  handled for you, and you're happy adopting the library and its tables.
- Reach for the **hand-rolled `outbox`** when you want full control of the relay, minimal
  dependencies, or a CDC-based relay, and you already have your own messaging setup.

## Rules
- Publish inside the CAP transaction, or you lose the atomicity that's the whole reason to use it.
- Consumers idempotent — at-least-once means duplicates.
- Match transport (Kafka/RabbitMQ) and storage (SqlServer/PostgreSQL/Mongo) to what the platform runs.
- Don't mix CAP and a separate hand-rolled outbox in the same service — pick one.
