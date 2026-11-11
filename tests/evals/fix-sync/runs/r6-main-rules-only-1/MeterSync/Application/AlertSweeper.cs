using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    // Longer than an SMS call can take (the client times out after 10 s). If a pod dies mid-send, another
    // pod picks the alert up once the claim expires.
    private static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(5);

    /// <summary>Sends the due alerts. Several pods can run this at once: each alert is claimed before it's sent.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The number of SMS this call sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var now = time.GetUtcNow();
        var due = await (
                from r in db.Readings
                join p in db.Patients on r.PatientId equals p.Id
                where r.Alert != AlertKind.None && r.AlertSentAt == null
                      && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now)
                orderby r.Id
                select new { Reading = r, p.Phone })
            .AsNoTracking()
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var item in due)
        {
            var reading = item.Reading;
            if (!PhoneNumber.TryToE164(item.Phone, out var to))
            {
                logger.LogError("Can't text the alert for reading {ReadingId}: patient {PatientId} has no valid phone number", reading.Id, reading.PatientId);
                continue;
            }

            if (!await TryClaimAsync(db, reading.Id, ct))
            {
                continue;
            }

            try
            {
                await sms.SendAsync(to, AlertText.For(reading), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Release the claim so the next run retries; one failed SMS must not block the others.
                logger.LogError(ex, "Sending the alert for reading {ReadingId} failed", reading.Id);
                await SetClaimAsync(db, reading.Id, null, CancellationToken.None);
                continue;
            }

            // Not cancellable: the SMS is out, so it must be recorded or it would be sent again.
            await db.Readings
                .Where(r => r.Id == reading.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.AlertSentAt, time.GetUtcNow())
                    .SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null), CancellationToken.None);
            sent++;
        }

        return sent;
    }

    private async Task<bool> TryClaimAsync(SyncDbContext db, int readingId, CancellationToken ct)
    {
        // A single conditional UPDATE: PostgreSQL re-checks the WHERE under the row lock, so only one pod wins.
        var now = time.GetUtcNow();
        var until = now + ClaimFor;
        var claimed = await db.Readings
            .Where(r => r.Id == readingId && r.AlertSentAt == null && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, until), ct);
        return claimed == 1;
    }

    private static Task<int> SetClaimAsync(SyncDbContext db, int readingId, DateTimeOffset? until, CancellationToken ct) =>
        db.Readings
            .Where(r => r.Id == readingId && r.AlertSentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, until), ct);
}
