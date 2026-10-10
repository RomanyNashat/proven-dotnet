---
name: cap-library
description: DotNetCore.CAP for .NET (per-project) — outbox and event bus in one library, on PostgreSQL and RabbitMQ — publishing in the same transaction, idempotent subscribers, the schema from a reviewed script instead of DDL at every start, and the defaults to change. Tested in CI against PostgreSQL and RabbitMQ 4.1.
version: 2.0.0
---

# DotNetCore.CAP: outbox and event bus, batteries included

`DotNetCore.CAP` (MIT) gives you the transactional outbox and a publish/subscribe bus in one package:
the outbox tables, the relay to the broker, retries, and `[CapSubscribe]` handlers. The `outbox` skill is
the same pattern built by hand. CAP 10 runs on .NET 10, with PostgreSQL, SQL Server or MongoDB for
storage and RabbitMQ or Kafka as the broker.

## Setup

<!-- sample: tests/SkillSamples.Tests/Cap/CapSetup.cs -->
```csharp
public static class CapSetup
{
    public static IServiceCollection AddEventBus(this IServiceCollection services, string postgres, Uri rabbitMq, string serviceName)
    {
        services.AddCap(x =>
        {
            x.UsePostgreSql(o => { o.ConnectionString = postgres; o.Schema = "cap"; });
            x.UseRabbitMQ(o =>
            {
                o.HostName = rabbitMq.Host;
                o.Port = rabbitMq.Port;
                (o.UserName, o.Password) = (rabbitMq.UserInfo.Split(':')[0], rabbitMq.UserInfo.Split(':')[1]);
                o.PublishConfirms = true;   // off by default: a message CAP marked as sent could be lost by the broker
            });
            x.DefaultGroupName = serviceName;    // the queue this service's subscribers read
            x.FailedRetryCount = 5;              // the default, 50, retries a broken message for most of an hour
        });

        // CAP creates its schema at every start (CREATE SCHEMA/TABLE IF NOT EXISTS), which needs DDL rights.
        // The schema comes from a reviewed script instead; at start-up the service only checks it's there.
        services.AddSingleton<IStorageInitializer, ReviewedSchemaCheck>();
        return services;
    }
}

public sealed class ReviewedSchemaCheck(IOptions<PostgreSqlOptions> options) : IStorageInitializer
{
    public string GetPublishedTableName() => $"\"{options.Value.Schema}\".\"published\"";
    public string GetReceivedTableName() => $"\"{options.Value.Schema}\".\"received\"";
    public string GetLockTableName() => $"\"{options.Value.Schema}\".\"lock\"";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var check = new NpgsqlCommand(
            "SELECT to_regclass(@published) IS NOT NULL AND to_regclass(@received) IS NOT NULL", connection);
        check.Parameters.AddWithValue("published", GetPublishedTableName());
        check.Parameters.AddWithValue("received", GetReceivedTableName());
        if (await check.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("CAP's tables are missing: apply the reviewed CAP schema script first.");
    }
}
```

**CAP changes the schema at every start, and hides it when that fails.** Its storage initializer runs
`CREATE SCHEMA/TABLE/INDEX IF NOT EXISTS` on each start, against the rule that the running app never
changes the schema (`rules/efcore-rules.md`). If that fails, CAP logs the error and starts anyway (only
an `InvalidOperationException` stops it). Tested as a story: in an environment where the script wasn't
applied, CAP's own initializer can't create the tables with the service's role, logs it, and keeps running;
the first publish would fail. With `ReviewedSchemaCheck` the service stops: CAP starts in a background
service, so the failure stops the host (and the pod restarts, failing the rollout) rather than failing
`StartAsync`.

So: apply CAP's schema as a deploy step, with a role that may create tables (the tests run CAP's own
`PostgreSqlStorageInitializer` once that way), and run the service with a role that may only read and
write `cap.published` and `cap.received`.

**The DBA's review:** tested, CAP's `Content` column is `text` and its times are `timestamp` without time
zone. A third-party schema you can't change: keep it in its own schema (`cap`) and record it as the narrow
exception the column rules allow, as for Hangfire and Quartz.

## Publish inside the transaction

<!-- sample: tests/SkillSamples.Tests/Cap/PlaceOrder.cs -->
```csharp
public sealed record OrderPlaced(int OrderId, string PatientName);

public sealed class PlaceOrder(NpgsqlDataSource db, ICapPublisher events)
{
    public const string Topic = "orders.placed";

    // The order row and the event commit together: CAP writes the event to its published table in the
    // same transaction, and sends it to the broker after the commit. A rollback sends nothing.
    public async Task<int> RunAsync(string patientName, bool failBeforeCommit, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(events, autoCommit: false, ct);
        try
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO orders (patient_name) VALUES (@name) RETURNING id", connection, (NpgsqlTransaction)transaction.DbTransaction!);
            insert.Parameters.AddWithValue("name", patientName);
            var id = (int)(await insert.ExecuteScalarAsync(ct))!;

            await events.PublishAsync(Topic, new OrderPlaced(id, patientName), cancellationToken: ct);

            if (failBeforeCommit)
                throw new InvalidOperationException("Something failed after the event was written.");

            await transaction.CommitAsync(ct);
            return id;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
```

Tested as stories:
- **An order is placed:** shipping gets the event, once.
- **Something fails after the event was written:** neither the order nor the event survives; nothing
  reaches the broker.

With EF Core: `using var transaction = await db.Database.BeginTransactionAsync(events, autoCommit: false)`,
then `SaveChangesAsync`, `PublishAsync`, `CommitAsync`. A `PublishAsync` outside a CAP transaction is a
plain send with no outbox. CAP's `BeginTransactionAsync` opens the database transaction synchronously
inside; it's short, but it's a blocking call on the request thread.

## Subscribers: at least once, so idempotent

<!-- sample: tests/SkillSamples.Tests/Cap/OrderPlacedSubscriber.cs -->
```csharp
public interface IShipments
{
    Task<bool> AlreadyHandledAsync(string messageId, CancellationToken ct);
    Task CreateAsync(OrderPlaced order, string messageId, CancellationToken ct);
}

// CAP delivers at least once: after a failure, or a crash before it recorded success, the same message
// comes again. The message id makes the second delivery a no-op.
public sealed class OrderPlacedSubscriber(IShipments shipments) : ICapSubscribe
{
    [CapSubscribe(PlaceOrder.Topic)]
    public async Task HandleAsync(OrderPlaced order, [FromCap] CapHeader header, CancellationToken ct)
    {
        var messageId = header[Headers.MessageId]!;
        if (await shipments.AlreadyHandledAsync(messageId, ct))
            return;
        await shipments.CreateAsync(order, messageId, ct);
    }
}
```

Register subscribers in DI (`services.AddTransient<OrderPlacedSubscriber>()`); CAP finds them there.
Tested as stories:
- **Shipping times out the first time:** CAP retries at once (up to three times before falling back to
  `FailedRetryInterval`), and one shipment is created.
- **The same message arrives twice:** the second is a no-op.

Store the handled message ids in the same transaction as the work (a unique index on them), so the check
and the write can't race.

Tested with no ICU: Arabic names arrive intact.

## CAP or the hand-rolled outbox
- **CAP** when you want the outbox, relay, retries and subscribers working now, and accept its tables and
  its conventions (topics, groups).
- **`outbox`** when you need full control of the relay, fewer dependencies, or CDC, or already have your
  own messaging.

Don't mix them in one service.

## Rules
- Publish inside the CAP transaction; a plain publish has no outbox.
- `PublishConfirms = true` with RabbitMQ; a `FailedRetryCount` you chose.
- The schema from a reviewed script; `ReviewedSchemaCheck` at start-up; the service role can't create tables.
- CAP's tables are a recorded exception to the column rules.
- Idempotent subscribers, keyed on the message id.
