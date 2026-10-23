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
        var lastId = 0;
        for (var attempts = 0; attempts < BatchSize; attempts++)
        {
            // The row stays locked while its SMS is sent, so another pod skips it instead of texting the patient twice.
            // A crash between the SMS and the commit re-sends it: a repeated alert is safer than a missing one.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.ChangeTracker.Clear();
            var found = await db.Readings
                .FromSql($"""
                    SELECT * FROM readings
                    WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL AND "Id" > {lastId}
                    ORDER BY "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);
            if (found.Count == 0)
            {
                break;
            }

            var reading = found[0];
            lastId = reading.Id;
            try
            {
                var phone = await db.Patients.AsNoTracking()
                    .Where(p => p.Id == reading.PatientId)
                    .Select(p => p.Phone)
                    .FirstOrDefaultAsync(ct);
                if (string.IsNullOrWhiteSpace(phone))
                {
                    logger.LogWarning("No phone for patient {PatientId}; alert for reading {ReadingId} not sent", reading.PatientId, reading.Id);
                    continue;
                }

                await sms.SendAsync(phone, AlertText.For(reading), ct);
                reading.MarkAlertSent(time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad reading must not block the alerts behind it; it is retried on the next run.
                logger.LogError(ex, "Alert for reading {ReadingId} failed", reading.Id);
            }
        }

        return sent;
    }
}
