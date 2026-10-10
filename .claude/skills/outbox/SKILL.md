---
name: outbox
description: Transactional outbox for .NET: the dual-write problem. EF Core on PostgreSQL and SQL Server (interceptor, HiLo ids, single ordered relay via an advisory lock or sp_getapplock) and MongoDB (transaction, ordered relay, lease), the inbox for idempotent consumers. All tested in CI.
---

# Transactional Outbox — reliable event publishing

Solves the **dual-write problem**: a service that saves business data *and* publishes an event has
two separate systems (its DB and the message broker) that can't commit atomically. Save the order →
crash before publishing → the event is lost forever, and no one downstream knows the order exists.
The outbox makes the event part of the same transaction as the data.

## The pattern
1. In the **same DB transaction** as the business write, insert a row into an `outbox` table
   describing the event (type, payload JSON, timestamp, status).
2. Commit. Now the data and the intent-to-publish are atomic — either both land or neither does.
3. A **background relay** reads unpublished outbox rows, publishes them to the broker (Kafka/RabbitMQ),
   and marks them published.

If the relay crashes mid-publish, the row is still `unpublished`, so it retries on restart. Nothing is
lost. The cost: the event is published **at-least-once** (a crash after publish but before marking
done → republish), so consumers must be **idempotent**.

## EF Core: PostgreSQL and SQL Server

The code in this section is compiled and run in CI against **both** PostgreSQL 17 and SQL Server 2022
(`tests/SkillSamples.Tests/EfOutbox/`); the same tests run on each engine: the row and the data commit
together or not at all, the relay keeps the order and stops at a failure, only one pod relays, and ten
simultaneous deliveries of one message change the data once.

Where the engines differ, the samples carry both branches so CI checks both. A service has one engine
and keeps only its own branch.

| | PostgreSQL | SQL Server |
|---|---|---|
| Payload | `jsonb` (allowed on PostgreSQL) | `nvarchar(4000)`: no `(max)`, so events carry ids, not documents |
| Timestamps | `timestamptz` | `datetime2(3)` in UTC, with a converter for `DateTimeOffset` (`efcore-patterns` §2) |
| Keys | `UseIdentityAlwaysColumn()`, HiLo sequence | `UseIdentityColumn()`, HiLo sequence |
| One relay | `pg_try_advisory_xact_lock` | `sp_getapplock` owned by the transaction |
| Inbox claim | `INSERT ... ON CONFLICT DO NOTHING` | `INSERT ... WHERE NOT EXISTS (... WITH (UPDLOCK, HOLDLOCK))` |

**The table** (DBA rules: bounded strings, UTC timestamps, `bigint` identity for a table that grows; the
column types per engine are in `ClinicDbContext.cs` in the samples folder):

<!-- sample: tests/SkillSamples.Tests/EfOutbox/OutboxMessage.cs -->
```csharp
public sealed class OutboxMessage
{
    public long Id { get; private set; }                    // bigint identity: the order, and the id consumers dedupe on
    public required string Type { get; init; }
    public required string AggregateKey { get; init; }      // the Kafka key: one aggregate's events stay in order
    public required string Payload { get; init; }           // jsonb; ids, not whole documents
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public static OutboxMessage From(IDomainEvent domainEvent, string aggregateKey, DateTimeOffset now) => new()
    {
        Type = domainEvent.GetType().Name,
        AggregateKey = aggregateKey,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        OccurredAt = now,
    };
}
```

**Events need the aggregate's id, which an identity column only assigns during the INSERT.** Give the
aggregate a HiLo key (`UseHiLo("appointments_hilo")`, still an `int`), and add it with `AddAsync` (HiLo
may fetch the next block of ids). Events are raised as factories and built when SaveChanges collects
them, by which time the id exists:

<!-- sample: tests/SkillSamples.Tests/EfOutbox/Aggregates.cs -->
```csharp
public interface IDomainEvent;

public abstract class AggregateRoot
{
    private readonly List<Func<IDomainEvent>> _events = [];

    public abstract string AggregateKey { get; }

    public bool HasDomainEvents => _events.Count > 0;

    // Events are built when SaveChanges collects them, not when they're raised, so they can carry an id
    // assigned in between: HiLo gives the aggregate its id when it's added to the context.
    public IReadOnlyList<IDomainEvent> CollectDomainEvents() => _events.Select(create => create()).ToList();

    public void ClearDomainEvents() => _events.Clear();

    protected void Raise(Func<IDomainEvent> domainEvent) => _events.Add(domainEvent);
}

public sealed record AppointmentBooked(int AppointmentId, int ClinicId, DateTimeOffset StartsAt) : IDomainEvent;

public sealed record AppointmentCancelled(int AppointmentId) : IDomainEvent;

public sealed class Appointment : AggregateRoot
{
    private Appointment()
    {
    }

    public int Id { get; private set; }
    public int ClinicId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public string? Notes { get; private set; }
    public bool IsCancelled { get; private set; }

    public override string AggregateKey => $"appointment-{Id}";

    public static Appointment Book(int clinicId, DateTimeOffset startsAt, string? notes)
    {
        var appointment = new Appointment { ClinicId = clinicId, StartsAt = startsAt, Notes = notes };
        appointment.Raise(() => new AppointmentBooked(appointment.Id, appointment.ClinicId, appointment.StartsAt));
        return appointment;
    }

    public void Cancel()
    {
        if (IsCancelled)
        {
            return;
        }

        IsCancelled = true;
        Raise(() => new AppointmentCancelled(Id));
    }
}
```

**The interceptor** writes the rows inside the same SaveChanges, so application code only raises events:

<!-- sample: tests/SkillSamples.Tests/EfOutbox/OutboxInterceptor.cs -->
```csharp
// Turns the domain events on tracked aggregates into outbox rows inside the same SaveChanges, so they
// commit or fail with the data. Application code raises events and never writes the outbox by hand.
public sealed class OutboxInterceptor(TimeProvider time) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddOutboxMessages(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AddOutboxMessages(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void AddOutboxMessages(DbContext? db)
    {
        if (db is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        var aggregates = db.ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.HasDomainEvents)
            .ToList();   // materialise first: adding rows below changes the tracker
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.CollectDomainEvents())
            {
                db.Set<OutboxMessage>().Add(OutboxMessage.From(domainEvent, aggregate.AggregateKey, now));
            }

            // Safe even if the save then fails: the rows stay tracked as Added, so a retried
            // SaveChanges (an execution strategy) still inserts them.
            aggregate.ClearDomainEvents();
        }
    }
}
```

Register it on the context: `options.UseNpgsql(cs)` (or `UseSqlServer(cs)`) `.AddInterceptors(new OutboxInterceptor(TimeProvider.System))`.

**The relay: one at a time, oldest first, stop at the first failure.**

<!-- sample: tests/SkillSamples.Tests/EfOutbox/OutboxRelay.cs -->
```csharp
public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

// Oldest first, stop at the first failure, and one relay at a time. Several relays with FOR UPDATE SKIP
// LOCKED don't publish a row twice, but they do break the order: one relay holds an appointment's first
// event while another skips past it and publishes the second.
public sealed class OutboxRelay(ClinicDbContext db, IOutboxPublisher publisher, TimeProvider time)
{
    private const long RelayLockKey = 7_410_001;   // any constant, unique per service database
    private const string RelayLockName = "outbox-relay";

    public async Task<int> PublishPendingAsync(int batchSize, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await TryTakeRelayLockAsync(ct))
        {
            return 0;   // another pod is relaying
        }

        var batch = await db.Outbox
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.GetType().Name;   // the type: a message can echo patient data
                break;                                   // keep the order: this one goes first next time
            }

            message.PublishedAt = time.GetUtcNow();
            published++;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return published;
    }

    /// <summary>
    /// A lock owned by the transaction: released at commit or rollback, and by the database if the pod
    /// dies. PostgreSQL: an advisory lock. SQL Server: sp_getapplock (0 or 1 = granted, negative = not).
    /// </summary>
    private async Task<bool> TryTakeRelayLockAsync(CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
        {
            return await db.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({RelayLockKey}) AS \"Value\"")
                .SingleAsync(ct);
        }

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @name, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0",
            [result, new SqlParameter("@name", RelayLockName)], ct);
        return (int)result.Value >= 0;
    }
}
```

Both locks belong to the transaction: they're released at commit or rollback, and by the database if
the pod dies, so a crashed relay never blocks the next one. On SQL Server, `sp_getapplock` returns 0 or 1
when granted and a negative number when not; `@LockTimeout = 0` means "don't wait".

The batch holds a transaction while it publishes, so keep batches small (tens, not thousands). Run it
from a `BackgroundService` on a `PeriodicTimer` (`worker-patterns`), with a new scope (and DbContext) per
run. The publisher sends `AggregateKey` as the Kafka key and the outbox `Id` in a header, which is what
consumers dedupe on (`kafka-patterns`).

**CDC instead of polling.** Debezium tailing the outbox table (logical replication on PostgreSQL, CDC on
SQL Server) removes the polling load and most of the latency, at the cost of running Debezium. Start
with polling; move when the latency or the load matters (`postgresql-patterns`, `sqlserver-patterns`).

## MongoDB

Same pattern, MongoDB shapes. The code in this section is compiled and run in CI against a real MongoDB 7
replica set (`tests/SkillSamples.Tests/MongoOutbox/`), including the failure cases.

**Two ways to make the event atomic with the data:**
- **An `outbox` collection, written in a multi-document transaction.** This is the general shape and
  works for any write. Transactions need a replica set, which production MongoDB is.
- **Pending events embedded in the changed document itself**, as an array on the aggregate. A
  single-document write is atomic without a transaction. It fits when one aggregate changes at a time;
  the relay then reads documents that have pending events and removes each one after publishing.

The rest of this section is the collection shape.

**The message** (the conventions: `ObjectId` ids, camelCase fields, enums as strings; see
`mongodb-patterns`):

<!-- sample: tests/SkillSamples.Tests/MongoOutbox/OutboxMessage.cs -->
```csharp
public sealed class OutboxMessage
{
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();   // also the event id consumers dedupe on
    public required string Type { get; init; }
    public required string AggregateId { get; init; }               // the Kafka key: one aggregate's events stay in order
    public required string Payload { get; init; }                   // JSON; keep it small, ids not documents
    public required DateTime OccurredAt { get; init; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public DateTime? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
```

**Writing it, in the same transaction as the data** (the callback API retries transient errors, so the
callback can run more than once; nothing inside it may call the broker):

<!-- sample: tests/SkillSamples.Tests/MongoOutbox/BookingService.cs -->
```csharp
        using var session = await client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _appointments.InsertOneAsync(s, appointment, cancellationToken: token);
            await _outbox.InsertOneAsync(s,
                OutboxMessage.For(booked, appointment.Id.ToString(), time.GetUtcNow().UtcDateTime),
                cancellationToken: token);
            return true;   // no broker call in here: the callback can run more than once
        }, cancellationToken: ct);
```
Tested: when the callback throws after both inserts, neither document is saved.

**The relay: oldest first, stop at the first failure.** Events of one aggregate must leave in order, so
the relay never skips past a message it failed to publish:

<!-- sample: tests/SkillSamples.Tests/MongoOutbox/OutboxRelay.cs -->
```csharp
        var pending = await _outbox
            .Find(m => m.Status == OutboxStatus.Pending)
            .SortBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Limit(batchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _outbox.UpdateOneAsync(m => m.Id == message.Id,
                    Builders<OutboxMessage>.Update.Inc(m => m.Attempts, 1).Set(m => m.LastError, ex.GetType().Name),
                    cancellationToken: ct);
                break;   // keep the order: retry this one first on the next run
            }
```
After publishing, the message is marked `Published` with `PublishedAt`. A crash between the two
publishes it again, which is why consumers dedupe on the message `Id` (below). Store the exception
**type** in `LastError`, never its message, which can echo patient data.

**One relay at a time.** With several pods, two relays would publish the same messages and break the
order. A lease document makes one pod the publisher; it renews the lease on each run, and if it dies,
another pod takes over when the lease expires:

<!-- sample: tests/SkillSamples.Tests/MongoOutbox/RelayLease.cs -->
```csharp
        var now = time.GetUtcNow().UtcDateTime;
        var filter = Builders<LeaseDocument>.Filter.Eq(l => l.Name, Name)
                   & (Builders<LeaseDocument>.Filter.Lt(l => l.ExpiresAt, now)
                      | Builders<LeaseDocument>.Filter.Eq(l => l.Owner, owner));
        var update = Builders<LeaseDocument>.Update
            .Set(l => l.Owner, owner)
            .Set(l => l.ExpiresAt, now + duration);
        try
        {
            await _leases.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<LeaseDocument> { IsUpsert = true }, ct);
            return true;
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            return false;   // someone else holds a live lease: the upsert hit the existing _id
        }
```
Run it from a `BackgroundService` on a `PeriodicTimer` (see `worker-patterns`): acquire or renew the
lease, then `PublishPendingAsync`. Keep the lease a few times longer than one run.

**Indexes, as a reviewed script** (the app never creates indexes at startup):
```javascript
// db-scripts/mongo/outbox-indexes.js — run by the pipeline/DBA with mongosh
db.outbox.createIndex({ occurredAt: 1, _id: 1 },
  { name: "ix_pending_occurredAt", partialFilterExpression: { status: "Pending" } });
db.outbox.createIndex({ publishedAt: 1 },
  { name: "ix_ttl_published_7d", expireAfterSeconds: 604800 });   // pending messages have no date, so they never expire
```
Tested: the relay's query uses `ix_pending_occurredAt`, and the stored field names match the script.

**Change streams instead of polling** (`mongodb-patterns` §8) cut the latency to near zero. But the
consumer must persist its resume token, still needs a single active reader to keep the order, and still
has to mark messages published. Start with polling; move only when the latency matters.

## The consumer side: the inbox (non-negotiable)

Delivery is at-least-once, so **every consumer must be idempotent**: processing a message twice must
have the same effect as once. The reliable way is an inbox row in the same transaction as the change:

<!-- sample: tests/SkillSamples.Tests/EfOutbox/Inbox.cs -->
```csharp
public sealed class InboxMessage
{
    public required string Consumer { get; init; }
    public required string MessageId { get; init; }
    public required DateTimeOffset ProcessedAt { get; init; }
}

public sealed class ClinicDailyCount
{
    public int ClinicId { get; init; }
    public DateOnly Day { get; init; }
    public int Booked { get; set; }
}

// An idempotent consumer. The inbox row and the change commit in one transaction, so a message is
// applied once however often it arrives. Two deliveries at the same moment: the second claim waits for
// the first transaction, then inserts nothing, and the handler skips. A service has one engine and
// keeps one branch.
public sealed class AppointmentBookedConsumer(ClinicDbContext db, TimeProvider time)
{
    private const string Name = "clinic-stats";

    public async Task<bool> HandleAsync(string messageId, AppointmentBooked booked, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow();
        // The clinic's own day: a 01:00 appointment in Riyadh is 22:00 UTC the day before.
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(booked.StartsAt, RiyadhTime.Zone).DateTime);
        if (db.Database.IsSqlServer())
        {
            // UPDLOCK + HOLDLOCK: a second delivery waits on the key range, then finds the row.
            var claimedHere = await db.Database.ExecuteSqlAsync($"""
                INSERT INTO inbox (consumer, message_id, processed_at)
                SELECT {Name}, {messageId}, {now.UtcDateTime}
                WHERE NOT EXISTS (SELECT 1 FROM inbox WITH (UPDLOCK, HOLDLOCK) WHERE consumer = {Name} AND message_id = {messageId})
                """, ct);
            if (claimedHere == 0)
            {
                return false;   // already applied
            }

            await db.Database.ExecuteSqlAsync($"""
                UPDATE clinic_daily_counts WITH (UPDLOCK, HOLDLOCK) SET booked = booked + 1 WHERE clinic_id = {booked.ClinicId} AND day = {day};
                IF @@ROWCOUNT = 0 INSERT INTO clinic_daily_counts (clinic_id, day, booked) VALUES ({booked.ClinicId}, {day}, 1);
                """, ct);
        }
        else
        {
            var claimed = await db.Database.ExecuteSqlAsync(
                $"INSERT INTO inbox (consumer, message_id, processed_at) VALUES ({Name}, {messageId}, {now}) ON CONFLICT DO NOTHING", ct);
            if (claimed == 0)
            {
                return false;   // already applied
            }

            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO clinic_daily_counts (clinic_id, day, booked) VALUES ({booked.ClinicId}, {day}, 1)
                ON CONFLICT (clinic_id, day) DO UPDATE SET booked = clinic_daily_counts.booked + 1
                """, ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }
}
```

Tested on both engines: ten deliveries of the same message at the same moment change the count once.
As a story: the broker is down all morning; bookings still save, the relay sends nothing, and when the
broker is back they go out oldest first; a redelivered event counts once.

**The day is the clinic's, not UTC's.** This consumer used to count by `StartsAt.UtcDateTime`, so a
01:30 booking in Riyadh landed on the day before. A production test (no tzdata, the pod in UTC) caught
it; the count now uses the clinic's time zone (`RiyadhTime`, `localization`).
On SQL Server, a plain `IF NOT EXISTS ... INSERT` is not enough: two transactions both see no row, and
one fails on the key. `UPDLOCK, HOLDLOCK` makes the second wait on the key range, then find the row. The inbox key is
(consumer, message id), so two consumers in one service can each process the same message. Clean the
table with a job that deletes rows older than the topic's retention; a message older than that can't be
redelivered.

Not this: "check Redis for the id, handle, then mark Redis". Two deliveries at once both pass the check.

## When to reach for it
- Any "save data AND publish an event" flow where losing the event is unacceptable (orders, payments,
  state changes other services depend on). If you run Kafka or RabbitMQ, you almost certainly have
  dual-writes that need this.
- For a turnkey library that bundles the outbox + relay, see DotNetCore.CAP (the `cap-library` per-project skill).

## See also
- **`efcore-patterns`** — §5 says why events are never published from a `SaveChanges` interceptor, and
  §1/§4 how interceptors work with pooled contexts.
- **`polly-resilience`** — the relay retries against a broker that will sometimes be unavailable.

## Rules
- Event row and business data commit in the **same transaction** — that's the whole point.
- Consumers are idempotent, always: an inbox row in the same transaction as the change.
- One relay at a time (an advisory lock on PostgreSQL, `sp_getapplock` on SQL Server, a lease on
  MongoDB). `SKIP LOCKED` / `READPAST` with several relays avoids duplicates but breaks the order of one
  aggregate's events.
- Don't put huge payloads in the outbox; store an id/reference if the payload is large.
