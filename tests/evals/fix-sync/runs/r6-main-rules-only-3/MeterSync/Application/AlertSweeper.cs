using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
public sealed class AlertSweeper(SyncDbContext db, SmsClient sms, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    /// <summary>How long a claim keeps other pods off an alert. A pod that dies mid-send loses it after this.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    /// <summary>Texts up to one batch of pending alerts.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many SMS were sent by this call.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var due = await db.Readings
            .Where(r => r.AlertStatus == AlertStatus.Pending && (r.AlertClaimedUntil == null || r.AlertClaimedUntil <= now))
            .OrderBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(ct);
        var patientIds = due.Select(r => r.PatientId).Distinct().ToList();
        var phones = await db.Patients
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        var sent = 0;
        foreach (var reading in due)
        {
            if (!await TryClaimAsync(reading, now, ct))
            {
                continue;
            }

            if (!phones.TryGetValue(reading.PatientId, out var phone) || string.IsNullOrWhiteSpace(phone))
            {
                logger.LogError("Reading {ReadingId} has no phone number to text", reading.Id);
                await RecordFailureAsync(reading);
                continue;
            }

            try
            {
                await sms.SendAsync(phone, AlertText.For(reading), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Texting the alert for reading {ReadingId} failed", reading.Id);
                await RecordFailureAsync(reading);
                continue;
            }

            reading.MarkAlertSent(time.GetUtcNow());

            // The SMS is out: record it even if we're shutting down, or the next sweep sends it again.
            await db.SaveChangesAsync(CancellationToken.None);
            sent++;
        }

        return sent;
    }

    /// <summary>Takes the alert for this pod. Fails when another pod changed the row since we read it.</summary>
    private async Task<bool> TryClaimAsync(Reading reading, DateTimeOffset now, CancellationToken ct)
    {
        if (!reading.TryClaimAlert(now, now + ClaimLease))
        {
            return false;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(reading).State = EntityState.Detached;
            return false;
        }
    }

    private async Task RecordFailureAsync(Reading reading)
    {
        if (reading.AlertFailed())
        {
            logger.LogError("Gave up texting the alert for reading {ReadingId} after {Attempts} attempts", reading.Id, reading.AlertAttempts);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }
}
