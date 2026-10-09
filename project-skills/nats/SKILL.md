---
name: nats
description: NATS pub/sub for .NET (pub/sub only): NATS.Net client, subjects, queue groups for load-balancing, wildcards, request/reply. Core pub/sub is at-most-once.
---

# NATS — pub/sub messaging for .NET

Lightweight, fast publish/subscribe over subjects. This skill is **scoped to pub/sub** — what most
platforms offer as a shared service — so it does not cover the full JetStream persistence/streaming surface
except where a pub/sub flow genuinely needs it. For durable event delivery with an outbox, see the
`outbox` / `cap-library` skills (different tool for a different job).

## The client
Use **NATS.Net** (the modern official .NET client, `NATS.Client.Core`). Register one connection and
reuse it — the connection is a multiplexed, long-lived object, not per-message.

```csharp
await using var nats = new NatsConnection(new NatsOpts { Url = "nats://nats:4222" });
```

## Publish
Subjects are dot-delimited names (`orders.created`, `patient.42.updated`). Publish a message to a
subject; every current subscriber to that subject receives it.
```csharp
await nats.PublishAsync("orders.created", new OrderCreated(order.Id));
```

## Subscribe
Subscribe to a subject and process the stream of messages:
```csharp
await foreach (var msg in nats.SubscribeAsync<OrderCreated>("orders.created"))
{
    await Handle(msg.Data);
}
```

## Queue groups (load-balanced consumers)
The key scaling primitive. Subscribers that share a **queue group** name have each message delivered
to **exactly one** member of the group — so N instances of a service split the load instead of all
processing every message. Without a queue group, every subscriber gets every message (fan-out).
```csharp
// Each message goes to ONE of the "order-workers", not all of them
await foreach (var msg in nats.SubscribeAsync<OrderCreated>("orders.created", queueGroup: "order-workers"))
    await Handle(msg.Data);
```
- **Fan-out** (notify everyone): no queue group.
- **Load-balance** (one worker handles it): same queue group across instances.

## Wildcards
- `*` matches one token: `patient.*.updated` → `patient.42.updated`, `patient.7.updated`.
- `>` matches one or more trailing tokens: `orders.>` → `orders.created`, `orders.eu.shipped`.

## Request/reply
NATS supports synchronous-style request/reply over subjects (one responder replies to the requester):
```csharp
var reply = await nats.RequestAsync<Query, Result>("patient.lookup", query);
```
Use for RPC-style calls where you want a direct response; use plain publish for fire-and-forget events.

## The honest constraint: core NATS pub/sub is at-most-once
Plain (core) NATS pub/sub does **not** persist messages. If no subscriber is connected when you
publish, or a subscriber is mid-restart, that message is **gone** — at-most-once delivery, no replay.
- Fine for: live signals, ephemeral notifications, cache-invalidation pings, presence — anything where
  a missed message is acceptable and the next one supersedes it.
- NOT fine for: events that must not be lost (orders, payments, state other services depend on). For
  those, use the transactional outbox (`outbox`/`cap-library`) over a durable broker, or NATS
  JetStream if the platform enables persistence — but core pub/sub alone is not durable.
Know which you're relying on; don't put must-not-lose events on core pub/sub.

## Rules
- One long-lived `NatsConnection`, reused — never per-message.
- Queue group = load-balance across instances; no queue group = fan-out to all. Pick deliberately.
- Core pub/sub is at-most-once — don't use it for events that can't be lost.
- Keep subjects hierarchical and stable (`domain.entity.event`); design wildcards around that shape.
