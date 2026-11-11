using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>
/// Texts the patient about each recent reading out of range that hasn't been sent yet. Several instances
/// may run this at once: each reading is claimed with a lease before its SMS goes out.
/// </summary>
public sealed class AlertSweeper(SyncDbContext db, SmsClient sms, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <returns>The number of alerts sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var candidates = await Due(time.GetUtcNow())
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var id in candidates)
        {
            try
            {
                if (await TrySendAsync(id, ct))
                {
                    sent++;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // The lease is left to expire, so a later sweep retries until MaxAlertAttempts parks it.
                logger.LogError(ex, "Alert for reading {ReadingId} failed", id);
            }
        }

        return sent;
    }

    private async Task<bool> TrySendAsync(int id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var claimedUntil = (DateTimeOffset?)(now + Lease);
        var claimed = await Due(now)
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.AlertClaimedUntil, claimedUntil)
                    .SetProperty(r => r.AlertAttempts, r => r.AlertAttempts + 1),
                ct);
        if (claimed != 1)
        {
            return false; // Another instance has it, or it was sent or parked since the batch was read.
        }

        var alert = await db.Readings
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Alert,
                r.Raw,
                r.Unit,
                r.TakenAt,
                Phone = db.Patients.Where(p => p.Id == r.PatientId).Select(p => p.Phone).FirstOrDefault(),
            })
            .SingleAsync(ct);
        if (string.IsNullOrWhiteSpace(alert.Phone))
        {
            logger.LogWarning("Alert for reading {ReadingId} skipped: the patient has no phone number", id);
            return false;
        }

        await sms.SendAsync(alert.Phone, AlertText.For(alert.Alert, alert.Raw, alert.Unit, alert.TakenAt), ct);

        // At-least-once: if the process dies between the send and this update, the lease expires and the
        // patient gets the SMS again. The gateway has no idempotency key, so a duplicate beats a lost alert.
        // CancellationToken.None so a shutdown after the send can't skip recording it.
        var sentAt = (DateTimeOffset?)time.GetUtcNow();
        await db.Readings
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertSentAt, sentAt), CancellationToken.None);
        return true;
    }

    /// <summary>Readings with an alert to send now: unsent, recent, not leased by anyone, not parked.</summary>
    private IQueryable<Reading> Due(DateTimeOffset now)
    {
        var since = now - Reading.AlertWindow;
        return db.Readings.Where(r =>
            r.Alert != AlertKind.None
            && r.AlertSentAt == null
            && r.TakenAt >= since
            && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now)
            && r.AlertAttempts < Reading.MaxAlertAttempts);
    }
}
