using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>
/// Sends waiting alerts to the care team every minute. The care team has no idempotency key, so the aim is
/// each alert sent once: every pod claims an alert before sending it, a send that has started is never cut
/// by shutdown, and each alert is marked in its own commit. One window is left: a pod that dies between
/// the POST and the mark sends that alert again once its claim runs out (at-least-once).
/// </summary>
public sealed class ReadingNotifier(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReadingNotifier> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>Longer than one send can take (the client's 30 s budget), so a live claim is never taken over.</summary>
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    private const int MaxAlertsPerRun = 50;

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
        var careTeam = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

        var sent = 0;
        var failed = 0;
        var tried = new List<int>();   // each alert at most once per run: a failed one waits for the next run
        while (tried.Count < MaxAlertsPerRun && !ct.IsCancellationRequested)
        {
            var alert = await ClaimNextAsync(db, tried, ct);
            if (alert is null)
            {
                break;
            }

            tried.Add(alert.Id);
            var delivered = await SendAsync(db, careTeam, alert);
            // The claim is a raw UPDATE that EF doesn't see, so a tracked alert would keep stale values.
            db.ChangeTracker.Clear();
            if (delivered)
            {
                sent++;
            }
            else
            {
                failed++;
            }
        }

        if (sent + failed > 0)
        {
            logger.LogInformation("Care team alerts: {Sent} sent, {Failed} failed", sent, failed);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A fault here would stop the whole host (BackgroundServiceExceptionBehavior.StopHost), API included.
                logger.LogError(ex, "Care team alert run failed; next run in {Interval}", Interval);
            }
        }
    }

    /// <summary>
    /// Claims the next due alert for this pod: least-tried first, skipping any row another pod is claiming
    /// right now, and any alert whose claim is still live.
    /// </summary>
    private async Task<CareTeamAlert?> ClaimNextAsync(ReadingsDbContext db, IReadOnlyCollection<int> tried, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var until = now + ClaimLease;
        var skip = tried.ToArray();
        var claimed = await db.Database.SqlQuery<int>($"""
            UPDATE care_team_alerts SET "ClaimedUntil" = {until}
            WHERE "Id" = (
                SELECT "Id" FROM care_team_alerts
                WHERE "SentAt" IS NULL AND ("ClaimedUntil" IS NULL OR "ClaimedUntil" < {now}) AND NOT ("Id" = ANY({skip}))
                ORDER BY "Attempts", "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);   // ToListAsync, not SingleAsync: an UPDATE can't be wrapped in a SELECT

        return claimed.Count == 0 ? null : await db.Alerts.SingleAsync(a => a.Id == claimed[0], ct);
    }

    /// <summary>
    /// Sends one claimed alert and records the outcome. Runs to the end once started, shutdown or not: a POST
    /// cut halfway may still have reached a nurse, and an alert left unmarked is sent again.
    /// </summary>
    private async Task<bool> SendAsync(ReadingsDbContext db, CareTeamClient careTeam, CareTeamAlert alert)
    {
        try
        {
            var reading = await db.Readings.AsNoTracking()
                .Where(r => r.Id == alert.ReadingId)
                .Select(r => new { r.NationalId, r.Value, r.Unit, r.CollectedAt })
                .SingleOrDefaultAsync(CancellationToken.None);
            if (reading is null)
            {
                alert.RecordFailure("ReadingNotFound");
                logger.LogError("Care team alert {AlertId} has no reading {ReadingId}", alert.Id, alert.ReadingId);
            }
            else
            {
                await careTeam.SendAlertAsync(
                    reading.NationalId, GlucoseUnits.ToMmolPerLitre(reading.Value, reading.Unit), reading.CollectedAt, CancellationToken.None);
                alert.MarkSent(time.GetUtcNow().UtcDateTime);
            }
        }
        catch (Exception ex)
        {
            // Recorded and retried on the next run, behind alerts tried fewer times. The type only: a
            // message can echo the request.
            alert.RecordFailure(ex.GetType().Name);
            logger.LogError(ex, "Care team alert {AlertId} failed, attempt {Attempts}", alert.Id, alert.Attempts);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return alert.SentAt is not null;
    }
}
