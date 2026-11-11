using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Every pod runs this. The gateway has no idempotency key, so each reading is claimed first with a
/// conditional UPDATE (only one pod's UPDATE matches), and only the claimer sends. A claim is a lease: if
/// the pod dies after claiming, another pod picks the reading up once the lease has run out.
/// </remarks>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    // Longer than an SMS call can take (the SmsClient timeout), so a live sender never loses its claim.
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    /// <summary>Sends the alerts that are due.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many alerts this run sent.</returns>
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
                logger.LogWarning("No phone for patient {PatientId}; alert for reading {ReadingId} not sent", reading.PatientId, reading.Id);
                continue;
            }

            if (!await TryClaimAsync(db, reading.Id, ct))
            {
                continue;
            }

            try
            {
                await sms.SendAsync(phone, AlertText.For(reading), ct);
            }
            catch (SmsRejectedException ex)
            {
                // The gateway answered that it didn't send: free the reading so the next run tries again.
                logger.LogError(ex, "SMS gateway rejected the alert for reading {ReadingId}", reading.Id);
                await db.Readings.Where(r => r.Id == reading.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null), CancellationToken.None);
                continue;
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
            {
                // No answer: the message may have gone out. Keep the claim so it is retried only after the lease,
                // not straight away by every pod.
                logger.LogError(ex, "SMS for reading {ReadingId} may not have been sent; retrying after the lease", reading.Id);
                continue;
            }

            // Sent: record it even if the run is being cancelled, or the patient gets the text twice.
            var sentAt = time.GetUtcNow();
            await db.Readings.Where(r => r.Id == reading.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertSentAt, sentAt), CancellationToken.None);
            sent++;
        }

        return sent;
    }

    private async Task<bool> TryClaimAsync(SyncDbContext db, int readingId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var until = now + ClaimLease;

        // The condition sits on the row itself, so under concurrent UPDATEs PostgreSQL re-checks it against the
        // committed row and only one pod gets a row count of 1.
        var claimed = await db.Readings
            .Where(r => r.Id == readingId && r.AlertSentAt == null
                        && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, until), ct);
        return claimed == 1;
    }
}
