using System.Linq.Expressions;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every recent reading out of range that hasn't been sent yet.</summary>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    /// <summary>
    /// Only readings this recent are texted. The vendor returns a meter's whole history, so linking a meter
    /// brings in weeks of old readings; a text about last month's low is noise, not something to act on.
    /// Older readings are stored but never texted.
    /// </summary>
    public static readonly TimeSpan AlertWindow = TimeSpan.FromHours(24);

    /// <summary>After this many attempts the alert is left for a person to follow up.</summary>
    public const int MaxAttempts = 5;

    private const int BatchSize = 100;

    // Long enough to cover one SMS call with its 10 s timeout; a crashed pod's claim expires and another retries.
    private static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(5);

    /// <summary>Sends the due alerts, safely alongside other instances.</summary>
    /// <param name="ct">Stops the run.</param>
    /// <returns>The number of SMS this call sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var due = await db.Readings.AsNoTracking()
            .Where(IsDue(time.GetUtcNow()))
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
            .Select(p => new { p.Id, p.Phone })
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        var sent = 0;
        foreach (var reading in due)
        {
            try
            {
                if (await TrySendAsync(db, sms, reading, phones.GetValueOrDefault(reading.PatientId), ct))
                {
                    sent++;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Alert for reading {ReadingId} failed", reading.Id);
            }
        }

        return sent;
    }

    private static Expression<Func<Reading, bool>> IsDue(DateTimeOffset now)
    {
        var windowStart = now - AlertWindow;
        return r => r.Alert != AlertKind.None
            && r.AlertSentAt == null
            && r.AlertAttempts < MaxAttempts
            && r.TakenAt >= windowStart
            && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now);
    }

    private async Task<bool> TrySendAsync(SyncDbContext db, SmsClient sms, Reading reading, string? phone, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        DateTimeOffset? claimUntil = now + ClaimFor;

        // The same conditions as the query, applied atomically: of several pods, only one gets the row.
        var claimed = await db.Readings
            .Where(r => r.Id == reading.Id)
            .Where(IsDue(now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.AlertClaimedUntil, claimUntil)
                .SetProperty(r => r.AlertAttempts, r => r.AlertAttempts + 1), ct);
        if (claimed != 1)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            logger.LogWarning("Reading {ReadingId} has no phone to alert", reading.Id);
            await ReleaseAsync(db, reading.Id, ct);
            return false;
        }

        try
        {
            await sms.SendAsync(phone, AlertText.For(reading), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Neither the phone nor the text is logged: both are patient data.
            logger.LogWarning("Alert SMS for reading {ReadingId} failed ({Error}); it will be retried", reading.Id, ex.GetType().Name);
            await ReleaseAsync(db, reading.Id, ct);
            return false;
        }

        // The SMS went out: record it even if the run is being cancelled, or the next run texts again.
        DateTimeOffset? sentAt = time.GetUtcNow();
        try
        {
            await db.Readings
                .Where(r => r.Id == reading.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.AlertSentAt, sentAt)
                    .SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Counted as sent because it was; the claim expires and the reading may be texted again.
            logger.LogError(ex, "Alert for reading {ReadingId} was sent but could not be recorded", reading.Id);
        }

        return true;
    }

    private static Task<int> ReleaseAsync(SyncDbContext db, int readingId, CancellationToken ct) =>
        db.Readings
            .Where(r => r.Id == readingId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null), ct);
}
