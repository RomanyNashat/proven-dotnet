using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>
/// Sends waiting alerts to the care team every minute. Several pods run this, and the care team has no
/// idempotency key, so each alert is claimed in the database before it is sent (only one pod wins a claim),
/// and is released for another try only when the send certainly did not arrive. When the outcome is unknown
/// the alert stays claimed and is reported every run until a person checks it with the care team.
/// </summary>
public sealed class ReadingNotifier(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReadingNotifier> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>Longer than the care team client's total timeout: a claim this old without a result is unknown.</summary>
    private static readonly TimeSpan UnknownAfter = TimeSpan.FromMinutes(5);

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
        var careTeam = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

        var waiting = await db.Alerts.AsNoTracking()
            .Where(a => a.SentAt == null && a.ClaimedAt == null)
            .OrderBy(a => a.Id)
            .Take(BatchSize)
            .Select(a => new { a.Id, a.Reading!.NationalId, a.Reading.Value, a.Reading.Unit, a.Reading.CollectedAt })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var alert in waiting)
        {
            if (ct.IsCancellationRequested || !await TryClaimAsync(db, alert.Id, ct))
            {
                continue;
            }

            // Once claimed, the send and its result are finished even if the host is stopping;
            // the client's own timeouts bound it.
            var delivery = await careTeam.SendAlertAsync(
                alert.NationalId, GlucoseUnits.ToMmolPerL(alert.Value, alert.Unit), alert.CollectedAt, CancellationToken.None);

            if (await RecordAsync(db, alert.Id, delivery))
            {
                sent++;
            }
        }

        if (sent > 0)
        {
            logger.LogInformation("Sent {Count} care team alerts", sent);
        }

        await ReportUnknownAsync(db, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // One bad run (database down, say) must not stop the loop, or the whole host with it.
                    logger.LogError(ex, "Sending care team alerts failed; trying again next minute");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> TryClaimAsync(ReadingsDbContext db, int alertId, CancellationToken ct)
    {
        DateTime? now = time.GetUtcNow().UtcDateTime;
        var claimed = await db.Alerts
            .Where(a => a.Id == alertId && a.SentAt == null && a.ClaimedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ClaimedAt, now), ct);
        return claimed == 1;
    }

    private async Task<bool> RecordAsync(ReadingsDbContext db, int alertId, AlertDelivery delivery)
    {
        try
        {
            switch (delivery)
            {
                case AlertDelivery.Delivered:
                    DateTime? sentAt = time.GetUtcNow().UtcDateTime;
                    await db.Alerts.Where(a => a.Id == alertId)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.SentAt, sentAt), CancellationToken.None);
                    return true;

                case AlertDelivery.NotDelivered:
                    await db.Alerts.Where(a => a.Id == alertId)
                        .ExecuteUpdateAsync(
                            s => s.SetProperty(a => a.ClaimedAt, (DateTime?)null)
                                .SetProperty(a => a.FailedAttempts, a => a.FailedAttempts + 1),
                            CancellationToken.None);
                    logger.LogWarning("Care team alert {AlertId} was not delivered; it will be sent again", alertId);
                    return false;

                default:
                    logger.LogError(
                        "Care team alert {AlertId} may or may not have reached the care team. It will not be resent automatically: check with the care team",
                        alertId);
                    return false;
            }
        }
        catch (Exception ex)
        {
            // Left claimed: if it was delivered, sending it again would alert a nurse twice.
            logger.LogError(ex, "Could not record the result ({Delivery}) of care team alert {AlertId}; it stays claimed", delivery, alertId);
            return delivery == AlertDelivery.Delivered;
        }
    }

    private async Task ReportUnknownAsync(ReadingsDbContext db, CancellationToken ct)
    {
        var claimedBefore = time.GetUtcNow().UtcDateTime - UnknownAfter;
        var unknown = await db.Alerts.CountAsync(a => a.SentAt == null && a.ClaimedAt != null && a.ClaimedAt < claimedBefore, ct);
        if (unknown > 0)
        {
            logger.LogError(
                "{Count} care team alerts have an unknown outcome. Check each with the care team, then set SentAt, or clear ClaimedAt to resend",
                unknown);
        }
    }
}
