using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>
/// Texts the patient about recent out-of-range readings that haven't been sent yet. Safe on several pods: a
/// reading is claimed with one conditional UPDATE before it is sent, so only one pod texts it.
/// </summary>
/// <remarks>
/// Accepted risk: a pod that crashes after the gateway accepted the text but before AlertSentAt is written
/// leaves the claim to expire, and the reading is texted again after <see cref="Reading.AlertLease"/>. The
/// gateway has no idempotency key, so this can't be closed from here.
/// </remarks>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    /// <summary>Sends one batch of due alerts.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The number of texts sent by this pod.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var now = time.GetUtcNow();
        var due = await db.Readings.AsNoTracking()
            .Where(Reading.DueForAlert(now))
            .OrderBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(ct);
        var patientIds = due.Select(r => r.PatientId).Distinct().ToList();
        var phones = await db.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        var sent = 0;
        foreach (var reading in due)
        {
            if (await TrySendAsync(db, sms, reading, phones, now, ct))
            {
                sent++;
            }
        }

        return sent;
    }

    private async Task<bool> TrySendAsync(
        SyncDbContext db, SmsClient sms, Reading reading, Dictionary<int, string> phones, DateTimeOffset now, CancellationToken ct)
    {
        // PostgreSQL re-checks the WHERE under the row lock, so of two pods claiming the same row only one gets 1.
        var leaseUntil = now + Reading.AlertLease;
        var claimed = await db.Readings
            .Where(r => r.Id == reading.Id)
            .Where(Reading.ClaimableForAlert(now))
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.AlertClaimedUntil, leaseUntil)
                      .SetProperty(r => r.AlertAttempts, r => r.AlertAttempts + 1),
                ct);
        if (claimed == 0)
        {
            return false;
        }

        var attempt = reading.AlertAttempts + 1;
        if (!phones.TryGetValue(reading.PatientId, out var phone) || string.IsNullOrWhiteSpace(phone))
        {
            logger.LogWarning("No phone number for the patient of reading {ReadingId}; alert not sent", reading.Id);
            return false;
        }

        try
        {
            await sms.SendAsync(phone, AlertText.For(reading), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The phone number stays out of the log; the reading id is enough to find it.
            if (attempt >= Reading.MaxAlertAttempts)
            {
                logger.LogError(ex, "Giving up on the alert for reading {ReadingId} after {Attempts} attempts", reading.Id, attempt);
            }
            else
            {
                logger.LogWarning(ex, "Alert for reading {ReadingId} failed (attempt {Attempt}); retried after the lease", reading.Id, attempt);
            }

            return false;
        }

        await db.Readings
            .Where(r => r.Id == reading.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertSentAt, time.GetUtcNow()), ct);
        return true;
    }
}
