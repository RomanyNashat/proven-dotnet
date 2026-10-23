using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var sent = 0;
        var failed = new List<int>();
        while (sent + failed.Count < BatchSize)
        {
            // Each reading is claimed under a row lock held until its SMS is sent and recorded, so pods
            // sweeping at the same time never text the same reading (SKIP LOCKED passes over rows another
            // pod holds). A crash between the send and the commit can repeat an SMS, which is preferable
            // to losing a low-glucose alert.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var skip = failed.ToArray();
            var reading = (await db.Readings
                .FromSqlInterpolated($"""
                    SELECT * FROM readings
                    WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL AND "Id" <> ALL({skip})
                    ORDER BY "Id" LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct)).FirstOrDefault();
            if (reading is null)
            {
                break;
            }

            try
            {
                var phone = await db.Patients.Where(p => p.Id == reading.PatientId).Select(p => p.Phone).SingleAsync(ct);
                await sms.SendAsync(phone, AlertText.For(reading), ct);
                reading.MarkAlertSent(time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad reading (unknown patient, rejected number) must not block the rest; it's retried next run.
                logger.LogError(ex, "Alert for reading {ReadingId} failed", reading.Id);
                failed.Add(reading.Id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return sent;
    }
}
