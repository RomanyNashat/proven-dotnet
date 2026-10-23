using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every recent reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Every pod runs this, and the SMS gateway has no idempotency key, so each reading is claimed with a
/// conditional UPDATE before its text is sent: only the pod whose UPDATE changed the row sends it. The
/// claim is a lease, so a pod that dies mid-send doesn't lose the alert; another pod sends it once the
/// lease runs out.
/// </remarks>
public sealed class AlertSweeper(SyncDbContext db, SmsClient sms, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    private static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(5);

    // A text about a reading from days ago (a meter's history on its first sync, or an outage) helps
    // nobody and alarms the patient.
    private static readonly TimeSpan AlertWindow = TimeSpan.FromHours(24);

    /// <summary>Sends the alerts that are due.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many texts this call sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var takenAfter = now - AlertWindow;
        var due = await db.Readings.AsNoTracking()
            .Where(r => r.Alert != AlertKind.None
                && r.AlertSentAt == null
                && r.TakenAt >= takenAfter
                && (r.AlertClaimedUntil == null || r.AlertClaimedUntil <= now))
            .OrderBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (due.Count == 0)
        {
            return 0;
        }

        var patientIds = due.Select(r => r.PatientId).Distinct().ToList();
        var phones = await db.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        var sent = 0;
        foreach (var reading in due)
        {
            if (!phones.TryGetValue(reading.PatientId, out var phone) || string.IsNullOrWhiteSpace(phone))
            {
                logger.LogWarning("No phone number for the patient of reading {ReadingId}", reading.Id);
                continue;
            }

            if (!await TryClaimAsync(reading.Id, ct))
            {
                continue;
            }

            try
            {
                await sms.SendAsync(phone, AlertText.For(reading), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Alert text for reading {ReadingId} failed; the next run retries it", reading.Id);
                await ReleaseAsync(reading.Id);
                continue;
            }

            // The text is out: record it even if shutdown has started, or it would be sent again.
            await db.Readings
                .Where(r => r.Id == reading.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertSentAt, time.GetUtcNow()), CancellationToken.None);
            sent++;
        }

        return sent;
    }

    private async Task<bool> TryClaimAsync(int readingId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var until = now + ClaimFor;
        var claimed = await db.Readings
            .Where(r => r.Id == readingId
                && r.AlertSentAt == null
                && (r.AlertClaimedUntil == null || r.AlertClaimedUntil <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, until), ct);
        return claimed == 1;
    }

    private Task<int> ReleaseAsync(int readingId) =>
        db.Readings
            .Where(r => r.Id == readingId && r.AlertSentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null), CancellationToken.None);
}
