using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Every pod runs this. A reading is claimed with a conditional UPDATE before its text is sent, so two
/// pods never both send it. The gateway has no idempotency key, so delivery is at least once: a pod that
/// dies after the gateway accepted a message but before it recorded the send leaves the claim to expire,
/// and the text goes again after <see cref="ClaimLease"/>.
/// </remarks>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    /// <summary>How long a claim lasts. Longer than the SMS client's timeout, so a slow send isn't taken over.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    private const int BatchSize = 100;

    /// <summary>Sends the alerts that are due.</summary>
    /// <param name="ct">Cancels the sweep.</param>
    /// <returns>How many texts were sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var now = time.GetUtcNow();
        var due = await db.Readings.AsNoTracking()
            .Where(r => r.Alert != AlertKind.None && r.AlertSentAt == null
                && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
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
            if (await TrySendAsync(db, sms, reading, phones.GetValueOrDefault(reading.PatientId), ct))
            {
                sent++;
            }
        }

        return sent;
    }

    private async Task<bool> TrySendAsync(SyncDbContext db, SmsClient sms, Reading reading, string? phone, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var claimedUntil = now + ClaimLease;
        var claimed = await db.Readings
            .Where(r => r.Id == reading.Id && r.AlertSentAt == null
                && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, claimedUntil), ct);
        if (claimed == 0)
        {
            return false; // another pod has it, or has sent it
        }

        var to = SaudiMobile.ToGatewayNumber(phone);
        if (to is null)
        {
            // No number or phone in the log: patient data. The reading id is enough to find it.
            logger.LogWarning("Alert for reading {ReadingId} not sent: the patient has no valid mobile number", reading.Id);
            await ReleaseAsync(db, reading.Id, CancellationToken.None);
            return false;
        }

        try
        {
            await sms.SendAsync(to, AlertText.For(reading), ct);
        }
        catch (HttpRequestException ex)
        {
            // The gateway answered with an error: nothing was sent, so let the next sweep try again.
            logger.LogWarning("Alert for reading {ReadingId} not sent: the SMS gateway answered {StatusCode}", reading.Id, ex.StatusCode);
            await ReleaseAsync(db, reading.Id, CancellationToken.None);
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // A timeout: the gateway may have sent it. Keep the claim, so it is retried only after the lease.
            logger.LogWarning("Alert for reading {ReadingId}: the SMS gateway timed out; retrying after the claim expires", reading.Id);
            return false;
        }

        // Recorded even if the sweep is being cancelled: the text has gone.
        var sentAt = time.GetUtcNow();
        await db.Readings
            .Where(r => r.Id == reading.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.AlertSentAt, sentAt).SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null),
                CancellationToken.None);
        return true;
    }

    private static Task<int> ReleaseAsync(SyncDbContext db, int readingId, CancellationToken ct) =>
        db.Readings
            .Where(r => r.Id == readingId && r.AlertSentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null), ct);
}
