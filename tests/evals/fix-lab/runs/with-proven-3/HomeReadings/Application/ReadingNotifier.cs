using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>
/// Sends waiting alerts to the care team every minute. The care team has no idempotency key, so:
/// one runner at a time across all pods (a PostgreSQL advisory lock); each alert is marked as being sent,
/// and that mark committed, before its POST; the POST is never retried; and an alert whose delivery is
/// unknown (a timeout, a dropped connection, a pod that died mid-send) is set aside for follow-up rather
/// than sent twice. Only an alert that certainly didn't arrive is sent again.
/// </summary>
public sealed class ReadingNotifier(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReadingNotifier> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 50;
    private const long RunLockKey = 4_130_027;   // any constant, unique within this service's database

    public async Task RunOnceAsync(CancellationToken ct)
    {
        // The lock lives in its own transaction on its own connection, so each alert's state below can
        // commit on its own. If the pod dies, the connection drops and the lock goes with it.
        await using var lockScope = scopes.CreateAsyncScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
        await using var runLock = await lockDb.Database.BeginTransactionAsync(ct);
        var acquired = await lockDb.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({RunLockKey}) AS \"Value\"")
            .SingleAsync(ct);
        if (!acquired)
        {
            return;   // another pod is sending
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
        var careTeam = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

        await SetAsideInterruptedAsync(db, ct);

        var waiting = await db.Alerts
            .Include(a => a.Reading)
            .Where(a => a.SentAt == null && a.NeedsFollowUpAt == null)
            .OrderBy(a => a.Attempts).ThenBy(a => a.Id)   // new alerts first; one that keeps failing can't hold them up
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var alert in waiting)
        {
            ct.ThrowIfCancellationRequested();
            alert.StartSending(Now());
            await db.SaveChangesAsync(ct);

            // Not cancelled on shutdown: a POST cut halfway has an unknown outcome. The attempt timeout
            // bounds it, well inside the host's shutdown timeout.
            var delivery = await careTeam.SendAlertAsync(ToMessage(alert.Reading!), CancellationToken.None);
            Record(alert, delivery);
            if (delivery.Outcome == AlertDeliveryOutcome.Delivered)
            {
                sent++;
            }

            // Whatever happened has happened: save it even if shutdown has started.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        if (sent > 0)
        {
            logger.LogInformation("Sent {Count} care team alerts", sent);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failed run (database or care team down) must not stop the host; the next tick tries again.
                logger.LogError(ex, "Care team alert run failed ({ExceptionType}); next run in {Interval}", ex.GetType().Name, Interval);
            }
        }
    }

    private static CareTeamAlertMessage ToMessage(Reading reading) => new(
        reading.NationalId,
        reading.ValueInMmolPerLitre,
        GlucoseUnit.MmolPerLitre,
        reading.OutOfRange switch { MeterLimit.High => "HI", MeterLimit.Low => "LO", _ => null },
        reading.CollectedAt);

    /// <summary>
    /// Only the lock holder sets <see cref="CareTeamAlert.SendingStartedAt"/>, so one still set now belongs
    /// to a run that died mid-send. The POST may have reached the care team: don't send it again.
    /// </summary>
    private async Task SetAsideInterruptedAsync(ReadingsDbContext db, CancellationToken ct)
    {
        var interrupted = await db.Alerts
            .Where(a => a.SentAt == null && a.NeedsFollowUpAt == null && a.SendingStartedAt != null)
            .ToListAsync(ct);
        foreach (var alert in interrupted)
        {
            alert.MarkNeedsFollowUp(Now(), "interrupted while sending");
            logger.LogError("Care team alert {AlertId} needs follow-up: the run sending it was interrupted, so it may or may not have arrived", alert.Id);
        }

        if (interrupted.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private void Record(CareTeamAlert alert, AlertDelivery delivery)
    {
        var reason = delivery.Reason ?? delivery.Outcome.ToString();
        switch (delivery.Outcome)
        {
            case AlertDeliveryOutcome.Delivered:
                alert.MarkSent(Now());
                break;
            case AlertDeliveryOutcome.Failed:
                alert.MarkNotDelivered(reason);
                logger.LogWarning("Care team alert {AlertId} not delivered ({Reason}); it is sent again next run", alert.Id, reason);
                break;
            default:
                alert.MarkNeedsFollowUp(Now(), reason);
                logger.LogError("Care team alert {AlertId} needs follow-up: {Outcome} ({Reason})", alert.Id, delivery.Outcome, reason);
                break;
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
