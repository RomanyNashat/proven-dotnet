using Microsoft.EntityFrameworkCore;

namespace SkillSamples.EfOutbox;

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
        var day = DateOnly.FromDateTime(booked.StartsAt.UtcDateTime);
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
