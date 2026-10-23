using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Several pods run this at once and the SMS gateway has no idempotency key, so every POST is a text. Each
/// reading is claimed with a conditional UPDATE before it is sent: only the pod whose UPDATE changed the row
/// sends. The claim is a lease. A send that fails keeps it, so the reading waits out the lease before it is
/// tried again (and doesn't hold up the readings behind it). A pod that dies after sending but before
/// recording it can cause one repeat text after the lease; a lost alert would be worse.
/// </remarks>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    private const int BatchSize = 100;

    /// <summary>Sends the due alerts once.</summary>
    /// <param name="ct">Stops the sweep.</param>
    /// <returns>How many texts were sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var cutoff = time.GetUtcNow() - ClaimLease;
        var due = await (
                from r in db.Readings.AsNoTracking()
                join p in db.Patients.AsNoTracking() on r.PatientId equals p.Id
                where r.Alert != AlertKind.None && r.AlertSentAt == null
                      && (r.AlertClaimedAt == null || r.AlertClaimedAt < cutoff)
                orderby r.Id
                select new { Reading = r, p.Phone })
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var item in due)
        {
            var id = item.Reading.Id;
            var now = time.GetUtcNow();
            var leaseCutoff = now - ClaimLease;
            var claimed = await db.Readings
                .Where(r => r.Id == id && r.AlertSentAt == null
                            && (r.AlertClaimedAt == null || r.AlertClaimedAt < leaseCutoff))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedAt, now), ct);
            if (claimed == 0)
            {
                continue; // another pod has it
            }

            try
            {
                await sms.SendAsync(item.Phone, AlertText.For(item.Reading), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Alert for reading {ReadingId} wasn't sent; it is tried again after the lease", id);
                continue;
            }

            var sentAt = time.GetUtcNow();
            await db.Readings
                .Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertSentAt, sentAt), CancellationToken.None);
            sent++;
        }

        return sent;
    }
}
