using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    public const int BatchSize = 100;
    public const int MaxAttempts = 5;

    // Long enough for one SMS POST (15 s timeout) plus the mark; a claim older than this belongs to a
    // pod that died, and the reading is due again.
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    /// <summary>Sends the due alerts once. Safe to run on several pods at the same time.</summary>
    /// <param name="ct">Cancels the sweep.</param>
    /// <returns>The number of messages sent by this call.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var expired = time.GetUtcNow() - ClaimLease;
        var due = await db.Readings
            .Where(r => r.Alert != AlertKind.None && r.AlertSentAt == null && r.AlertAttempts < MaxAttempts
                && (r.AlertClaimedAt == null || r.AlertClaimedAt < expired))
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var id in due)
        {
            try
            {
                sent += await SendAsync(db, sms, id, ct) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // A rejected number or a gateway timeout must not block every later alert; the reading is
                // retried after the lease, up to MaxAttempts.
                logger.LogError(ex, "Alert for reading {ReadingId} failed", id);
            }
        }

        return sent;
    }

    private async Task<bool> SendAsync(SyncDbContext db, SmsClient sms, int id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var expired = now - ClaimLease;

        // The claim is the only step that decides who sends: of two pods, exactly one updates the row.
        var claimed = await db.Readings
            .Where(r => r.Id == id && r.AlertSentAt == null && r.AlertAttempts < MaxAttempts
                && (r.AlertClaimedAt == null || r.AlertClaimedAt < expired))
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.AlertClaimedAt, now).SetProperty(r => r.AlertAttempts, r => r.AlertAttempts + 1),
                ct);
        if (claimed != 1)
        {
            return false;
        }

        var reading = await db.Readings.AsNoTracking().SingleAsync(r => r.Id == id, ct);
        var phone = await db.Patients.Where(p => p.Id == reading.PatientId).Select(p => p.Phone).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(phone))
        {
            logger.LogWarning("Alert for reading {ReadingId} skipped: patient {PatientId} has no phone", id, reading.PatientId);
            return false;
        }

        await sms.SendAsync(phone, AlertText.For(reading), ct);

        // The text is out: record it even if the host is stopping, or the next sweep sends it again.
        // Accepted residual: a pod that dies between the POST and this update resends once after the lease
        // (the gateway has no idempotency key, so this can't be closed from our side).
        await db.Readings
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertSentAt, time.GetUtcNow()), CancellationToken.None);
        return true;
    }
}
