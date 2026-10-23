using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>
/// Texts the patient about every reading out of range that hasn't been sent yet. Runs on every pod: each run
/// claims its own batch (AlertClaims), so a patient gets one SMS, not one per pod.
/// </summary>
/// <remarks>
/// The gateway has no idempotency key, so delivery is at least once: a pod that dies between sending and
/// marking, or whose mark fails to save, leaves the row claimed, and it is sent once more after the claim
/// lapses. A lock held over the sends instead would roll back every mark of the batch on a crash and resend
/// all of it.
/// </remarks>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    public const int BatchSize = 100;

    /// <summary>How long a claim holds. Longer than a batch takes; the run stops early if it gets close.</summary>
    public static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan StopBeforeClaimLapses = TimeSpan.FromMinutes(1);

    /// <summary>Claims a batch of pending alerts and texts each one.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How many SMS were sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var now = time.GetUtcNow();
        var claimUntil = now + ClaimFor;
        var claimed = await db.ClaimDueAsync(now, claimUntil, BatchSize, ct);
        if (claimed.Count == 0)
        {
            return 0;
        }

        var due = await db.Readings.Where(r => claimed.Contains(r.Id)).OrderBy(r => r.Id).ToListAsync(ct);
        var patientIds = due.Select(r => r.PatientId).Distinct().ToList();
        var phones = await db.Patients
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        var sent = 0;
        foreach (var reading in due)
        {
            if (time.GetUtcNow() >= claimUntil - StopBeforeClaimLapses)
            {
                break;   // the rest go back to the pool when the claim lapses, rather than being sent twice
            }

            if (!phones.TryGetValue(reading.PatientId, out var phone) || string.IsNullOrWhiteSpace(phone))
            {
                logger.LogWarning("Alert for reading {ReadingId} not sent: the patient has no phone number", reading.Id);
                reading.GiveUpAlert(time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                await sms.SendAsync(phone, AlertText.For(reading), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // One failure doesn't stop the batch. The type only: the gateway's error can echo the number.
                reading.RecordAlertFailure(time.GetUtcNow());
                logger.LogWarning("Alert for reading {ReadingId} failed with {ErrorType} (attempt {Attempt} of {MaxAttempts})",
                    reading.Id, ex.GetType().Name, reading.AlertAttempts, Domain.Reading.MaxAlertAttempts);
                await db.SaveChangesAsync(ct);
                continue;
            }

            reading.MarkAlertSent(time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            sent++;
        }

        return sent;
    }
}
