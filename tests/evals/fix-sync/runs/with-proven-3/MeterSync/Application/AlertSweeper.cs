using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Every pod runs this. Each claims its own batch with FOR UPDATE SKIP LOCKED and a lease, so two pods never
/// pick the same alert. An alert is marked sent right after the gateway accepts it. The gateway has no
/// idempotency key, so one case still sends twice: a pod that dies between the POST and that save. Its lease
/// runs out and another pod sends the alert again. A duplicate there is the price of never losing an alert.
/// </remarks>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 20;

    // Longer than a whole batch can take: 20 sends at the SMS client's 10 s timeout is under 4 minutes.
    // A lease that ran out mid-batch would let another pod claim and send the same alerts.
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    // Doubles after each failure (5, 10, 20, 40 minutes): an alert is parked after about 75 minutes of
    // failures, so a short gateway outage only delays it.
    private static readonly TimeSpan FirstRetryAfter = TimeSpan.FromMinutes(5);

    // The literal 'Pending' matches the partial index ix_readings_pending_alerts; a parameter there wouldn't.
    private const string ClaimBatch = """
        UPDATE readings SET "AlertClaimedUntil" = @leaseUntil
        WHERE "Id" IN (
            SELECT "Id" FROM readings
            WHERE "AlertDelivery" = 'Pending' AND ("AlertClaimedUntil" IS NULL OR "AlertClaimedUntil" <= @now)
            ORDER BY "Id"
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED)
        RETURNING "Id" AS "Value"
        """;

    /// <summary>Sends every pending alert this pod can claim, batch by batch.</summary>
    /// <returns>How many SMS were sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var sent = 0;
        while (!ct.IsCancellationRequested)
        {
            var claimed = await ClaimAsync(db, ct);
            if (claimed.Count == 0)
            {
                break;
            }

            sent += await SendAsync(db, sms, claimed, ct);
            db.ChangeTracker.Clear();
        }

        return sent;
    }

    private async Task<List<int>> ClaimAsync(SyncDbContext db, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await db.Database.SqlQueryRaw<int>(
                ClaimBatch,
                new NpgsqlParameter("leaseUntil", NpgsqlDbType.TimestampTz) { Value = now + Lease },
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now },
                new NpgsqlParameter("batchSize", NpgsqlDbType.Integer) { Value = BatchSize })
            .ToListAsync(ct);
    }

    private async Task<int> SendAsync(SyncDbContext db, SmsClient sms, List<int> claimed, CancellationToken ct)
    {
        var due = await db.Readings.Where(r => claimed.Contains(r.Id)).OrderBy(r => r.Id).ToListAsync(ct);
        var patientIds = due.Select(r => r.PatientId).Distinct().ToList();
        var phones = await db.Patients
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        var sent = 0;
        foreach (var reading in due)
        {
            var error = await TrySendAsync(sms, reading, phones, ct);
            if (error is null)
            {
                reading.MarkAlertSent(time.GetUtcNow());
                sent++;
            }
            else
            {
                reading.RecordAlertFailure(error, time.GetUtcNow() + (FirstRetryAfter * (1 << reading.AlertAttempts)));
                if (reading.AlertDelivery == AlertDelivery.Failed)
                {
                    logger.LogError("Alert for reading {ReadingId} parked after {Attempts} attempts: {Error}", reading.Id, reading.AlertAttempts, error);
                }
                else
                {
                    logger.LogWarning("Alert for reading {ReadingId} failed (attempt {Attempts}): {Error}", reading.Id, reading.AlertAttempts, error);
                }
            }

            // Saved one by one, and not cancelled by shutdown: once the gateway has the SMS, recording it is
            // what stops the next pod from sending it again.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return sent;
    }

    /// <returns>Null when the gateway took the SMS; otherwise what went wrong, with no patient data in it.</returns>
    private static async Task<string?> TrySendAsync(SmsClient sms, Reading reading, Dictionary<int, string> phones, CancellationToken ct)
    {
        if (!phones.TryGetValue(reading.PatientId, out var phone))
        {
            return "PatientNotFound";
        }

        try
        {
            await sms.SendAsync(phone, AlertText.For(reading), ct);
            return null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return ex is HttpRequestException { StatusCode: { } status }
                ? $"{nameof(HttpRequestException)} {(int)status}"
                : ex.GetType().Name;
        }
    }
}
