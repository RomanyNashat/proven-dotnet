using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every recent reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Several pods run this at once and the SMS gateway has no idempotency key, so each reading is claimed with
/// an atomic conditional UPDATE before it is sent: only the pod whose UPDATE matched sends it. A failed send
/// keeps its claim until the lease runs out, then any pod retries it (until the reading is too old).
/// </remarks>
public sealed class AlertSweeper(SyncDbContext db, SmsClient sms, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    // Longer than one SMS call can take (the client times out well before), so a live claim is never stolen.
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    /// <summary>Sends the due alerts once.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many texts this run sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var cutoff = now - Reading.AlertWindow;

        await db.Readings
            .Where(r => r.AlertState == AlertState.Pending && r.TakenAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertState, AlertState.Expired), ct);

        var due = await (
                from r in db.Readings.AsNoTracking()
                join p in db.Patients on r.PatientId equals p.Id
                where r.AlertState == AlertState.Pending && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now)
                orderby r.Id
                select new { Reading = r, p.Phone })
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var item in due)
        {
            if (!await TryClaimAsync(item.Reading.Id, now, ct))
            {
                continue;
            }

            try
            {
                await sms.SendAsync(item.Phone, AlertText.For(item.Reading), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogError(ex, "Sending the alert for reading {ReadingId} failed; it will be retried", item.Reading.Id);
                continue;
            }

            // The text has gone out: record it even if we are shutting down, or another pod would send it again.
            await db.Readings
                .Where(r => r.Id == item.Reading.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(r => r.AlertState, AlertState.Sent).SetProperty(r => r.AlertSentAt, time.GetUtcNow()),
                    CancellationToken.None);
            sent++;
        }

        return sent;
    }

    private async Task<bool> TryClaimAsync(int readingId, DateTimeOffset now, CancellationToken ct) =>
        await db.Readings
            .Where(r => r.Id == readingId
                        && r.AlertState == AlertState.Pending
                        && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, now + ClaimLease), ct) == 1;
}
