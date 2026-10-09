---
name: kafka-patterns
description: Kafka for .NET with Confluent.Kafka: idempotent producer keyed by aggregate, at-least-once consumer (store-after-handle, retry, dead-letter topic), routing, idempotent handling. Core code tested in CI. See outbox for reliable publishing.
version: 1.1.0
---

# Kafka Patterns

The producer and the consumer below are compiled and tested in CI against a real Kafka 3.9
(`tests/SkillSamples.Tests/Kafka`): a transient failure is retried in order, a poison message goes to
the dead-letter topic while the rest flows, and a pod stopped mid-message replays that message and
nothing before it.

## Topic Naming Convention

```
<domain>.<entity>.<event>

Examples:
  orders.order.created
  orders.order.shipped
  orders.order.cancelled
  inventory.stock.reserved
  inventory.stock.released
  notifications.notification.sent
  notifications.notification.failed
```

Rules:
- Lowercase with dots as separators
- `<domain>` = bounded context / service name
- `<entity>` = aggregate or entity type
- `<event>` = what happened (past tense)
- Dead letter: append `.dlq` → `orders.order.created.dlq`
- Retry: append `.retry` → `orders.order.created.retry`

## Producer

<!-- sample: tests/SkillSamples.Tests/Kafka/KafkaPublisher.cs -->
```csharp
public sealed class KafkaPublisher(IProducer<string, string> producer)
{
    public static ProducerConfig Config(string bootstrapServers) => new()
    {
        BootstrapServers = bootstrapServers,
        EnableIdempotence = true,   // the client's own retries can't duplicate or reorder within a partition
        Acks = Acks.All,
        LingerMs = 5,
        CompressionType = CompressionType.Lz4,
        MessageTimeoutMs = 30_000,  // bounds every internal retry; don't wrap ProduceAsync in a Polly retry too
    };

    // The key picks the partition, and order is kept only within a partition. Key by the aggregate
    // (the appointment id), never a random value, or one appointment's events can arrive out of order.
    public Task<DeliveryResult<string, string>> PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct) =>
        producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = JsonSerializer.Serialize(@event),
            Headers = new Headers { { "event-type", Encoding.UTF8.GetBytes(typeof(TEvent).Name) } },
        }, ct);
}
```

One producer per process (`AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(KafkaPublisher.Config(servers)).Build())`):
it's thread-safe and batches across callers. Dispose it on shutdown so buffered messages are flushed
(`producer.Flush(timeout)` first, if the host gives you time).

Publishing from a request handler after `SaveChanges` is a dual write: the save can succeed and the
publish fail. Anything that must not be lost goes through the outbox (`outbox`).

## Consumer: at-least-once

<!-- sample: tests/SkillSamples.Tests/Kafka/KafkaConsumerWorker.cs -->
```csharp
public sealed record KafkaConsumerOptions
{
    public required string BootstrapServers { get; init; }
    public required string GroupId { get; init; }
    public required string Topic { get; init; }
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}

public interface IKafkaMessageHandler
{
    Task HandleAsync(ConsumeResult<string, string> message, CancellationToken ct);
}

// At-least-once: a message's offset is stored only after it was handled (or parked on the dead-letter
// topic), and stored offsets are committed in the background, on rebalance and on close. A crash replays
// what wasn't stored, so handlers must be idempotent (the inbox in the outbox skill).
public sealed class KafkaConsumerWorker(
    KafkaConsumerOptions options,
    IProducer<string, string> producer,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();   // Consume() blocks; on .NET 8 don't block host startup with it
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,        // commits what was stored, every 5 s
            EnableAutoOffsetStore = false,  // ...and only we store, after handling
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(options.Topic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                await HandleWithRetryAsync(result, stoppingToken);
                consumer.StoreOffset(result);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down; the message in hand (if any) wasn't stored and will be redelivered
        }
        finally
        {
            consumer.Close();   // commits the stored offsets and leaves the group, so partitions move at once
        }
    }

    // Retries stay short: the whole loop must finish well inside max.poll.interval.ms (5 minutes by
    // default) or the broker assumes the consumer died and hands its partitions to another pod.
    private async Task HandleWithRetryAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IKafkaMessageHandler>().HandleAsync(result, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < options.MaxAttempts)
            {
                logger.LogWarning("Attempt {Attempt} failed for {Topic}[{Partition}]@{Offset}: {Error}",
                    attempt, result.Topic, result.Partition.Value, result.Offset.Value, ex.GetType().Name);
                await Task.Delay(options.RetryDelay * attempt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Out of attempts: park it and move on, so one bad message doesn't stop the partition.
                // If this write fails it throws, the offset isn't stored, and the message comes back.
                await producer.ProduceAsync($"{result.Topic}.dlq", new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = result.Message.Value,
                    Headers = new Headers
                    {
                        { "dlq-source", Encoding.UTF8.GetBytes($"{result.Topic}[{result.Partition.Value}]@{result.Offset.Value}") },
                        { "dlq-error", Encoding.UTF8.GetBytes(ex.GetType().FullName ?? "unknown") },   // the type, not the message: it can hold patient data
                    },
                }, ct);
                logger.LogError("Parked {Topic}[{Partition}]@{Offset} on the dead-letter topic after {Attempts} attempts: {Error}",
                    result.Topic, result.Partition.Value, result.Offset.Value, attempt, ex.GetType().Name);
                return;
            }
        }
    }
}
```

What this replaced, and why:
- **A synchronous `Commit()` after every message.** Correct, but a broker round trip per message.
  Storing offsets after handling, with auto-commit of what's stored, gives the same at-least-once
  guarantee in the background.
- **`GroupInstanceId = Environment.MachineName`** (static membership). On a Deployment the pod name
  changes every rollout, so the old member isn't recognised: its partitions sit unassigned until the
  session times out, on every pod of every deploy. Static membership is for StatefulSets with stable
  names; leave it off otherwise.
- **Logging the exception message, and copying it to the dead-letter message.** Handler exceptions
  often echo the payload. Log and record the type; the payload is already in the dead-letter message.

Dead-letter topics are `<topic>.dlq`. Someone owns reading them: an alert on a non-empty DLQ, and a way
to replay a fixed message back to the main topic.

## Routing by event type

```csharp
// Built once at startup: event-type header -> typed handler. No reflection per message.
public sealed class EventRoutes
{
    private readonly Dictionary<string, Func<IServiceProvider, string, CancellationToken, Task>> _routes = new(StringComparer.Ordinal);

    public EventRoutes Map<TEvent>() where TEvent : class
    {
        _routes[typeof(TEvent).Name] = (sp, json, ct) =>
            sp.GetRequiredService<IIntegrationEventHandler<TEvent>>().HandleAsync(JsonSerializer.Deserialize<TEvent>(json)!, ct);
        return this;
    }

    public bool TryGet(string eventType, out Func<IServiceProvider, string, CancellationToken, Task> route) =>
        _routes.TryGetValue(eventType, out route!);
}

public sealed class EventRouter(IServiceProvider services, EventRoutes routes, ILogger<EventRouter> logger) : IKafkaMessageHandler
{
    public Task HandleAsync(ConsumeResult<string, string> message, CancellationToken ct)
    {
        var eventType = message.Message.Headers.TryGetLastBytes("event-type", out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
        if (eventType is null || !routes.TryGet(eventType, out var route))
        {
            logger.LogDebug("Skipping {EventType} on {Topic}: no handler here", eventType ?? "(none)", message.Topic);
            return Task.CompletedTask;   // shared topics carry events this service doesn't use
        }

        return route(services, message.Message.Value, ct);
    }
}

// Program.cs
builder.Services.AddSingleton(new EventRoutes().Map<AppointmentBooked>().Map<AppointmentCancelled>());
builder.Services.AddScoped<IKafkaMessageHandler, EventRouter>();
builder.Services.AddScoped<IIntegrationEventHandler<AppointmentBooked>, AppointmentBookedHandler>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Kafka:Consumer").Get<KafkaConsumerOptions>()!);
builder.Services.AddHostedService<KafkaConsumerWorker>();
```

A JSON payload that fails to deserialize is a poison message: it throws, is retried, and goes to the
dead-letter topic. That's the intended path.

## Idempotent handling

At-least-once means a message can arrive twice (a crash before the offset was committed, a rebalance).
The handler has to make the second delivery harmless:
- **Best: record the message id in the same database transaction as the change** (an inbox table with a
  unique key). If the insert hits the unique key, it's a duplicate: skip it. The change and the record
  commit together, so there's no window between them. See `outbox` → the inbox.
- **Or make the change naturally idempotent:** an upsert keyed by the event's id, or a state change that
  is a no-op when already applied (`SET status = 'Cancelled' WHERE id = @id AND status <> 'Cancelled'`).
- **Avoid "check Redis, handle, then mark Redis".** Two deliveries at once both pass the check, and a
  crash between handling and marking replays the message anyway.

## See also — reliable publishing (outbox)
If a producer needs to save DB data AND publish an event atomically, don't dual-write directly to
Kafka — use the **transactional outbox** (`outbox` skill): write the event to an outbox table in the
same DB transaction, relay to Kafka from a background worker (polling or CDC). Delivery is
at-least-once, so consumers must be idempotent. For a turnkey library, see DotNetCore.CAP (the `cap-library` per-project skill — copy it in if used).
- **`rabbitmq-patterns`** (per-project, not installed) — only if a service really needs RabbitMQ.
  Kafka is the default broker here.

