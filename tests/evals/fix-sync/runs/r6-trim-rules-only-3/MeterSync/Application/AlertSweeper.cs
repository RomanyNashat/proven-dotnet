using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every recent reading out of range that hasn't been sent yet.</summary>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    // While one pod sends an alert, no other pod may. If that pod dies mid-send, another retries after this.
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    // A meter uploads its whole history when first linked; texting a patient about a reading from last month
    // helps no one. Older readings are stored but not texted.
    private static readonly TimeSpan MaxAlertAge = TimeSpan.FromHours(6);

    /// <summary>Sends due alerts. Safe to run on several pods at once: each reading is texted by one pod.</summary>
    /// <param name="ct">Stops the run.</param>
    /// <returns>How many alerts this call sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var now = time.GetUtcNow();
        var oldest = now - MaxAlertAge;
        var due = await db.Readings.AsNoTracking()
            .Where(r => r.Alert != AlertKind.None && r.AlertSentAt == null && r.TakenAt >= oldest
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
            if (!phones.TryGetValue(reading.PatientId, out var phone) || string.IsNullOrWhiteSpace(phone))
            {
                logger.LogWarning("No phone for the patient of reading {ReadingId}; alert not sent", reading.Id);
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
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
            {
                // The claim stays until it expires, so the next sweep after the lease retries this one.
                // The gateway has no idempotency key, so a send that failed after the gateway took it can repeat;
                // for a glucose alert a rare duplicate is safer than a missed one.
                logger.LogWarning(ex, "Alert for reading {ReadingId} failed; retried after the claim expires", reading.Id);
                continue;
            }

            // The SMS is out: record it even if the run is being cancelled, or it would be sent again.
            var sentAt = time.GetUtcNow();
            await db.Readings
                .Where(r => r.Id == reading.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertSentAt, sentAt), CancellationToken.None);
            sent++;
        }

        return sent;
    }

    // Atomic in PostgreSQL: of two pods updating the same row, the second re-checks the WHERE after the first
    // commits and updates nothing.
    private async Task<bool> TryClaimAsync(SyncDbContext db, int readingId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var until = now + ClaimLease;
        var claimed = await db.Readings
            .Where(r => r.Id == readingId && r.AlertSentAt == null
                        && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, until), ct);
        return claimed == 1;
    }
}
