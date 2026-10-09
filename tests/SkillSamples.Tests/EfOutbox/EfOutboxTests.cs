using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SkillSamples.EfOutbox;

public sealed class RecordingPublisher : IOutboxPublisher
{
    public ConcurrentQueue<long> Published { get; } = new();
    public Func<OutboxMessage, Task>? Before { get; set; }

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        if (Before is not null)
        {
            await Before(message);
        }

        Published.Enqueue(message.Id);
    }
}

/// <summary>The same checks on both engines.</summary>
public abstract class EfOutboxTests<TFixture>(TFixture pg) where TFixture : ClinicFixture
{
    private static readonly DateTimeOffset Tomorrow = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    private async Task<int> BookAsync(int clinicId, string? notes = null)
    {
        await using var db = pg.NewContext();
        var appointment = Appointment.Book(clinicId, Tomorrow, notes);
        await db.Appointments.AddAsync(appointment);   // HiLo may fetch the next block: use AddAsync
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task ClearOutboxAsync()
    {
        await using var db = pg.NewContext();
        await db.Outbox.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Save_WritesTheOutboxRowWithTheData_AndTheEventCarriesTheId()
    {
        await ClearOutboxAsync();
        var id = await BookAsync(clinicId: 4);

        await using var db = pg.NewContext();
        var row = await db.Outbox.SingleAsync(m => m.AggregateKey == $"appointment-{id}");
        Assert.True(id > 0);
        Assert.Equal(nameof(AppointmentBooked), row.Type);
        Assert.Equal(id, JsonSerializer.Deserialize<AppointmentBooked>(row.Payload)!.AppointmentId);
        Assert.Null(row.PublishedAt);
    }

    [Fact]
    public async Task FailedSave_LeavesNeitherTheDataNorTheEvent()
    {
        int outboxBefore;
        await using (var before = pg.NewContext())
        {
            outboxBefore = await before.Outbox.CountAsync();
        }

        await using (var db = pg.NewContext())
        {
            await db.Appointments.AddAsync(Appointment.Book(5, Tomorrow, notes: new string('x', 300)));   // over varchar(200)
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var check = pg.NewContext();
        Assert.False(await check.Appointments.AnyAsync(a => a.ClinicId == 5));
        Assert.Equal(outboxBefore, await check.Outbox.CountAsync());
    }

    [Fact]
    public async Task Relay_PublishesOldestFirst_AndStopsAtTheFirstFailure()
    {
        await ClearOutboxAsync();
        for (var i = 0; i < 3; i++)
        {
            await BookAsync(clinicId: 6);
        }

        long secondId;
        await using (var peek = pg.NewContext())
        {
            secondId = (await peek.Outbox.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync())[1];
        }

        var failOnce = true;
        var publisher = new RecordingPublisher
        {
            Before = m =>
            {
                if (failOnce && m.Id == secondId)
                {
                    failOnce = false;
                    throw new TimeoutException("broker unavailable");
                }

                return Task.CompletedTask;
            },
        };

        await using (var db = pg.NewContext())
        {
            Assert.Equal(1, await new OutboxRelay(db, publisher, TimeProvider.System).PublishPendingAsync(10, default));
        }

        await using (var db = pg.NewContext())
        {
            Assert.Equal(1, (await db.Outbox.SingleAsync(m => m.Id == secondId)).Attempts);
            Assert.Equal(2, await new OutboxRelay(db, publisher, TimeProvider.System).PublishPendingAsync(10, default));
        }

        var order = publisher.Published.ToArray();
        Assert.Equal(order.OrderBy(x => x).ToArray(), order);
        Assert.Equal(3, order.Length);
    }

    [Fact]
    public async Task Relay_OnlyOnePodRelaysAtATime()
    {
        await ClearOutboxAsync();
        await BookAsync(clinicId: 7);
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var slow = new RecordingPublisher { Before = async _ => { entered.TrySetResult(); await release.Task; } };

        await using var podA = pg.NewContext();
        var relayA = new OutboxRelay(podA, slow, TimeProvider.System).PublishPendingAsync(10, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await using (var podB = pg.NewContext())
        {
            Assert.Equal(0, await new OutboxRelay(podB, new RecordingPublisher(), TimeProvider.System).PublishPendingAsync(10, default));
        }

        release.SetResult();
        Assert.Equal(1, await relayA);
    }

    [Fact]
    public async Task Inbox_TenConcurrentDeliveries_AreAppliedOnce()
    {
        var booked = new AppointmentBooked(AppointmentId: 99, ClinicId: 8, Tomorrow);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = pg.NewContext();
            return await new AppointmentBookedConsumer(db, TimeProvider.System).HandleAsync("outbox-99", booked, default);
        }));

        Assert.Single(results, applied => applied);
        await using var check = pg.NewContext();
        Assert.Equal(1, (await check.DailyCounts.SingleAsync(c => c.ClinicId == 8)).Booked);
    }
}

public sealed class PostgresOutboxTests(PostgresClinic pg) : EfOutboxTests<PostgresClinic>(pg), IClassFixture<PostgresClinic>;

public sealed class SqlServerOutboxTests(SqlServerClinic db) : EfOutboxTests<SqlServerClinic>(db), IClassFixture<SqlServerClinic>;
