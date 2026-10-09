using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace SkillSamples.MongoOutbox;

public sealed class MongoOutboxTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private sealed class RecordingPublisher : IOutboxPublisher
    {
        public List<string> Published { get; } = [];
        public Func<OutboxMessage, bool> FailWhen { get; set; } = _ => false;

        public Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            if (FailWhen(message)) throw new TimeoutException("broker unavailable");
            Published.Add(message.AggregateId);
            return Task.CompletedTask;
        }
    }

    private static readonly DateTime Start = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Book_SavesAppointmentAndOutboxMessageTogether()
    {
        var db = mongo.NewDatabase();
        await MongoFixture.CreateOutboxIndexesAsync(db);
        var service = new BookingService(mongo.Client, db, new FakeTimeProvider(Start));

        var appointment = await service.BookAsync(7, Start.AddDays(1), CancellationToken.None);

        var raw = await db.GetCollection<BsonDocument>("outbox").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal("Pending", raw["status"].AsString);                 // matches the partial index filter
        Assert.Equal(appointment.Id.ToString(), raw["aggregateId"].AsString);
        Assert.Equal("AppointmentBooked", raw["type"].AsString);
        Assert.True(raw.Contains("occurredAt"));                        // matches the index key
        Assert.Equal(1, await db.GetCollection<Appointment>("appointments").CountDocumentsAsync(FilterDefinition<Appointment>.Empty));
    }

    [Fact]
    public async Task Transaction_ThatFails_SavesNeither()
    {
        var db = mongo.NewDatabase();
        await MongoFixture.CreateOutboxIndexesAsync(db);
        var appointments = db.GetCollection<Appointment>("appointments");
        var outbox = db.GetCollection<OutboxMessage>("outbox");

        using var session = await mongo.Client.StartSessionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.WithTransactionAsync<bool>(async (s, token) =>
        {
            var appointment = new Appointment { ClinicId = 1, Slot = Start };
            await appointments.InsertOneAsync(s, appointment, cancellationToken: token);
            await outbox.InsertOneAsync(s, OutboxMessage.For(new { appointment.ClinicId }, appointment.Id.ToString(), Start), cancellationToken: token);
            throw new InvalidOperationException("business rule failed after both writes");
        }));

        Assert.Equal(0, await appointments.CountDocumentsAsync(FilterDefinition<Appointment>.Empty));
        Assert.Equal(0, await outbox.CountDocumentsAsync(FilterDefinition<OutboxMessage>.Empty));
    }

    [Fact]
    public async Task Relay_PublishesOldestFirst_AndOnlyOnce()
    {
        var db = mongo.NewDatabase();
        await MongoFixture.CreateOutboxIndexesAsync(db);
        var outbox = db.GetCollection<OutboxMessage>("outbox");
        await outbox.InsertManyAsync([
            OutboxMessage.For(new { n = 2 }, "b", Start.AddSeconds(2)),
            OutboxMessage.For(new { n = 1 }, "a", Start.AddSeconds(1)),
            OutboxMessage.For(new { n = 3 }, "c", Start.AddSeconds(3)),
        ]);
        var publisher = new RecordingPublisher();
        var relay = new OutboxRelay(db, publisher, new FakeTimeProvider(Start.AddMinutes(1)));

        Assert.Equal(3, await relay.PublishPendingAsync(100, CancellationToken.None));
        Assert.Equal(0, await relay.PublishPendingAsync(100, CancellationToken.None));
        Assert.Equal(new[] { "a", "b", "c" }, publisher.Published);
        Assert.Equal(3, await outbox.CountDocumentsAsync(m => m.Status == OutboxStatus.Published && m.PublishedAt != null));
    }

    [Fact]
    public async Task Relay_StopsAtFirstFailure_SoNothingOvertakesIt()
    {
        var db = mongo.NewDatabase();
        await MongoFixture.CreateOutboxIndexesAsync(db);
        var outbox = db.GetCollection<OutboxMessage>("outbox");
        await outbox.InsertManyAsync([
            OutboxMessage.For(new { n = 1 }, "a", Start.AddSeconds(1)),
            OutboxMessage.For(new { n = 2 }, "b", Start.AddSeconds(2)),
            OutboxMessage.For(new { n = 3 }, "c", Start.AddSeconds(3)),
        ]);
        var publisher = new RecordingPublisher { FailWhen = m => m.AggregateId == "b" };
        var relay = new OutboxRelay(db, publisher, new FakeTimeProvider(Start.AddMinutes(1)));

        Assert.Equal(1, await relay.PublishPendingAsync(100, CancellationToken.None));
        var failed = await outbox.Find(m => m.AggregateId == "b").SingleAsync();
        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal("TimeoutException", failed.LastError);

        publisher.FailWhen = _ => false;
        Assert.Equal(2, await relay.PublishPendingAsync(100, CancellationToken.None));
        Assert.Equal(new[] { "a", "b", "c" }, publisher.Published);
    }

    [Fact]
    public async Task Lease_HasOneHolder_UntilItExpires()
    {
        var db = mongo.NewDatabase();
        var time = new FakeTimeProvider(Start);
        var podA = new RelayLease(db, time, "pod-a", TimeSpan.FromSeconds(30));
        var podB = new RelayLease(db, time, "pod-b", TimeSpan.FromSeconds(30));

        Assert.True(await podA.TryAcquireAsync(CancellationToken.None));
        Assert.False(await podB.TryAcquireAsync(CancellationToken.None));
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.True(await podA.TryAcquireAsync(CancellationToken.None));    // renew
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.False(await podB.TryAcquireAsync(CancellationToken.None));   // renewed lease still live
        time.Advance(TimeSpan.FromSeconds(31));
        Assert.True(await podB.TryAcquireAsync(CancellationToken.None));    // pod-a stopped renewing
        Assert.False(await podA.TryAcquireAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PendingQuery_UsesThePartialIndex()
    {
        var db = mongo.NewDatabase();
        await MongoFixture.CreateOutboxIndexesAsync(db);
        var explain = await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument
                {
                    { "find", "outbox" },
                    { "filter", new BsonDocument("status", "Pending") },
                    { "sort", new BsonDocument { { "occurredAt", 1 }, { "_id", 1 } } },
                    { "limit", 100 },
                }
            },
            { "verbosity", "queryPlanner" },
        });
        Assert.Contains("ix_pending_occurredAt", explain["queryPlanner"]["winningPlan"].ToJson());
    }
}
