using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>
/// Sends waiting alerts to the care team every minute. The care team's system has no idempotency key,
/// so every POST pages a nurse: an alert is claimed by one instance (row lock + lease) before it is sent,
/// and the HTTP client does not retry POSTs. A failed send is released and tried again on the next run.
/// </summary>
public sealed class ReadingNotifier(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReadingNotifier> logger) : BackgroundService
{
    // Each send is bounded by the HTTP attempt timeout (10 s), so a batch finishes well inside the lease.
    private const int BatchSize = 10;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
        var careTeam = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

        var claimed = await ClaimAsync(db, ct);
        if (claimed.Count == 0)
        {
            return;
        }

        var ids = claimed.Select(a => a.ReadingId).ToList();
        var readings = await db.Readings.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        var sent = 0;
        foreach (var alert in claimed)
        {
            if (!readings.TryGetValue(alert.ReadingId, out var reading))
            {
                logger.LogError("Care team alert {AlertId} has no reading {ReadingId}", alert.Id, alert.ReadingId);
                alert.Release();
                await db.SaveChangesAsync(CancellationToken.None);
                continue;
            }

            try
            {
                var value = reading.Unit == GlucoseUnit.MmolPerLitre
                    ? reading.Value
                    : Math.Round(reading.ValueInMmolPerLitre, 1, MidpointRounding.AwayFromZero);
                await careTeam.SendAlertAsync(reading.NationalId, value, reading.CollectedAt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Care team alert {AlertId} not sent (attempt {Attempt}); will retry", alert.Id, alert.Attempts + 1);
                alert.Release();
                await db.SaveChangesAsync(CancellationToken.None);
                continue;
            }

            // Saved without the request's token: the nurse has been paged, so this must be recorded.
            alert.MarkSent(time.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(CancellationToken.None);
            sent++;
        }

        logger.LogInformation("Sent {Sent} of {Claimed} care team alerts", sent, claimed.Count);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // An unhandled exception here would stop the host; a database blip must not take the API down.
                logger.LogError(ex, "Care team notifier run failed");
            }
        }
    }

    /// <summary>
    /// Takes the next waiting alerts nobody else holds. SKIP LOCKED keeps two pods from taking the same
    /// rows; the lease keeps them from taking an alert another pod is still sending.
    /// </summary>
    private async Task<List<CareTeamAlert>> ClaimAsync(ReadingsDbContext db, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var until = now + Lease;
        var claimed = await db.Alerts.FromSql($"""
            UPDATE care_team_alerts SET "ClaimedUntil" = {until}
            WHERE "Id" IN (
                SELECT "Id" FROM care_team_alerts
                WHERE "SentAt" IS NULL AND ("ClaimedUntil" IS NULL OR "ClaimedUntil" < {now})
                ORDER BY "Id"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).ToListAsync(ct);
        return [.. claimed.OrderBy(a => a.Id)];
    }
}
