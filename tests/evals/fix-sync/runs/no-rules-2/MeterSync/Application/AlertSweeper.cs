using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every recent reading out of range that hasn't been sent yet.</summary>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int MaxPerRun = 100;

    // An alert about an old reading is noise (e.g. a newly linked meter uploads its whole history), so those are never sent.
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var sent = 0;
        var failed = new List<int>();
        while (sent + failed.Count < MaxPerRun)
        {
            db.ChangeTracker.Clear();

            // The row lock is held until commit, so two pods never text the same reading; SKIP LOCKED lets them share the backlog.
            // The gateway has no idempotency key, so the reading is only marked sent after the gateway accepted it.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var cutoff = time.GetUtcNow() - MaxAge;
            var skip = failed.ToArray();
            var reading = (await db.Readings.FromSql($"""
                SELECT * FROM readings
                WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL AND "TakenAt" >= {cutoff} AND "Id" <> ALL({skip})
                ORDER BY "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct)).SingleOrDefault();
            if (reading is null)
            {
                break;
            }

            try
            {
                var phone = await db.Patients.Where(p => p.Id == reading.PatientId).Select(p => p.Phone).SingleOrDefaultAsync(ct);
                if (string.IsNullOrWhiteSpace(phone))
                {
                    throw new InvalidOperationException($"Patient {reading.PatientId} has no phone number");
                }

                await sms.SendAsync(phone, AlertText.For(reading), ct);
                reading.MarkAlertSent(time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                sent++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Don't let one reading we can't deliver block the others; it is retried on the next run.
                logger.LogError(ex, "Alert for reading {ReadingId} failed", reading.Id);
                failed.Add(reading.Id);
            }
        }

        return sent;
    }
}
