using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Several pods sweep the same table and the SMS gateway has no idempotency key, so a reading is
/// claimed (a lease in <c>AlertClaimedAt</c>) before it is sent. The claim is one autocommitted
/// statement with <c>FOR UPDATE SKIP LOCKED</c>: two pods never claim the same row, and no
/// transaction stays open across the HTTP call. A pod that dies mid-batch leaves its claims to
/// expire, and another pod picks them up after <see cref="Lease"/>.
/// </remarks>
public sealed class AlertSweeper(SyncDbContext db, SmsClient sms, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    // Stop sending this long before the lease ends, so a slow last send (SMS timeout is 15 s)
    // can't still be running when another pod is allowed to claim the same row.
    private static readonly TimeSpan LeaseMargin = TimeSpan.FromMinutes(1);

    /// <summary>Sends up to one batch of due alerts.</summary>
    /// <returns>How many messages the gateway accepted.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var expired = now - Lease;
        var claimed = await db.Database.SqlQuery<int>(
                $"""
                UPDATE readings SET "AlertClaimedAt" = {now}
                WHERE "Id" IN (
                    SELECT "Id" FROM readings
                    WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL
                      AND ("AlertClaimedAt" IS NULL OR "AlertClaimedAt" < {expired})
                    ORDER BY "Id"
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED)
                RETURNING "Id" AS "Value"
                """)
            .ToListAsync(ct);
        if (claimed.Count == 0)
        {
            return 0;
        }

        var due = await db.Readings.Where(r => claimed.Contains(r.Id)).OrderBy(r => r.Id).ToListAsync(ct);
        var patientIds = due.Select(r => r.PatientId).Distinct().ToList();
        var phones = await db.Patients
            .AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        var deadline = now + Lease - LeaseMargin;
        var sent = 0;
        foreach (var reading in due)
        {
            if (time.GetUtcNow() >= deadline)
            {
                // Hand the rest back now rather than let the lease run out under us.
                reading.ReleaseAlertClaim();
                continue;
            }

            try
            {
                await sms.SendAsync(phones[reading.PatientId], AlertText.For(reading), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // No phone number or alert value in the log: both are personal data.
                logger.LogError(ex, "Alert SMS failed for reading {ReadingId}; released for the next sweep", reading.Id);
                reading.ReleaseAlertClaim();
                await db.SaveChangesAsync(ct);
                continue;
            }

            reading.MarkAlertSent(time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            sent++;
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }
}
