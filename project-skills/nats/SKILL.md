---
name: nats
description: NATS pub/sub for .NET with NATS.Net — one client that can send records, queue groups to share work, fan-out, wildcards, request/reply with a timeout, and why core pub/sub loses what nobody is listening for. Tested in CI against NATS 2.10.
version: 2.0.0
---

# NATS: pub/sub messaging for .NET

Fast publish/subscribe over subjects. This skill covers core pub/sub, which is what most platforms run
as a shared service. For events that must not be lost, see `outbox` or `cap-library`; NATS JetStream adds
persistence when the platform enables it.

## The client

<!-- sample: tests/SkillSamples.Tests/Nats/NatsSetup.cs -->
```csharp
public sealed record OrderCreated(int OrderId);

public static class NatsSetup
{
    // One long-lived client per service. NatsClient (package NATS.Net) sends records as JSON; a bare
    // NatsConnection with default options handles only strings, numbers and bytes, and throws on a record.
    public static IServiceCollection AddNats(this IServiceCollection services, string url, string serviceName) =>
        services.AddSingleton<INatsClient>(_ => new NatsClient(new NatsOpts { Url = url, Name = serviceName }));
}
```

Tested as a story: the old version of this skill created `new NatsConnection(new NatsOpts { Url = ... })`
and published a record. That throws `NatsException: Can't serialize`. `NatsClient` adds JSON; with a
bare connection, set `SerializerRegistry = NatsJsonSerializerRegistry.Default`.

## Subscribing: queue groups share the work

<!-- sample: tests/SkillSamples.Tests/Nats/OrderCreatedWorker.cs -->
```csharp
public interface IOrderCreatedHandler
{
    Task HandleAsync(OrderCreated order, CancellationToken ct);
}

// Every instance of the service runs this. The queue group gives each message to one of them; without
// it, every instance would handle every order.
public sealed class OrderCreatedWorker(INatsClient nats, IOrderCreatedHandler handler) : BackgroundService
{
    public const string Subject = "orders.created";
    public const string QueueGroup = "order-workers";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<OrderCreated>(Subject, QueueGroup, cancellationToken: stoppingToken))
        {
            if (msg.Data is { } order)
                await handler.HandleAsync(order, stoppingToken);
        }
    }
}
```

Tested as stories:
- **Three instances share a queue group:** 30 orders, each handled once, spread over more than one
  instance.
- **Two services subscribe without one** (billing and shipping): both get every event. Fan-out is the
  default; sharing is what the queue group adds.
- **The worker runs as a hosted service** and handles a published order.

`msg.Data` is null when a message can't be read as the type (or has no body); check it.

## Wildcards

- `*` matches exactly one token: `patient.*.updated` matches `patient.42.updated`, not
  `patient.42.notes.updated`.
- `>` matches one or more trailing tokens: `orders.>` matches `orders.eu.shipped`, not `orders` itself.

All four cases tested. Keep subjects shaped `domain.entity.event`, so wildcards line up with them.

## Request/reply

<!-- sample: tests/SkillSamples.Tests/Nats/PatientLookup.cs -->
```csharp
public sealed record LookupPatient(int PatientId);
public sealed record PatientSummary(int PatientId, string Name);

public static class PatientLookup
{
    public const string Subject = "patient.lookup";

    // The responder: every request gets one reply.
    public static async Task ServeAsync(INatsClient nats, Func<int, PatientSummary> find, CancellationToken ct)
    {
        await foreach (var request in nats.SubscribeAsync<LookupPatient>(Subject, queueGroup: "patient-lookup", cancellationToken: ct))
        {
            if (request.Data is { } lookup)
                await request.ReplyAsync(find(lookup.PatientId), cancellationToken: ct);
        }
    }

    // The caller: a timeout, because a request with no answer would otherwise wait for the default.
    public static async Task<PatientSummary?> AskAsync(INatsClient nats, int patientId, CancellationToken ct)
    {
        var reply = await nats.RequestAsync<LookupPatient, PatientSummary>(
            Subject, new LookupPatient(patientId), replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(2) }, cancellationToken: ct);
        return reply.Data;
    }
}
```

`RequestAsync` returns a `NatsMsg<T>`; the answer is its `Data`. Tested as a story: a responder answers,
and a request with no responder fails at once with `NatsNoRespondersException` instead of waiting out
the timeout.

## Core pub/sub is at most once

Core NATS keeps nothing. Tested as a story: an event published while nobody is subscribed is gone; the
subscriber that starts a moment later gets only the next one. The same happens to a subscriber that's
restarting during a deploy.
- Fine for live signals, cache-invalidation pings, presence: a missed one is replaced by the next.
- Not fine for orders, payments, or state other services depend on. Use an outbox over a durable broker
  (`outbox`, `cap-library`), or JetStream where the platform runs it.

Tested with no ICU: Arabic text in a reply arrives intact.

## Rules
- One `NatsClient` per service, registered as a singleton.
- Queue group to share work across instances; none to fan out. Choose on purpose.
- Requests with a timeout; handle `NatsNoRespondersException`.
- Core pub/sub only for messages that can be lost.
