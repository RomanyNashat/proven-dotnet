using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>Sends waiting alerts to the care team every minute.</summary>
public sealed class ReadingNotifier(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReadingNotifier> logger) : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
        var careTeam = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

        var waiting = await db.Alerts.AsNoTracking()
            .Where(a => a.SentAt == null)
            .OrderBy(a => a.Id)
            .Take(50)
            .Join(db.Readings, a => a.ReadingId, r => r.Id, (a, r) => new { AlertId = a.Id, r.NationalId, r.Value, r.Unit, r.CollectedAt })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var alert in waiting)
        {
            // The care team has no idempotency key, so a second POST pages a nurse again. Claim the alert first
            // (only one instance can win this update), then send; give the claim back only if the send failed.
            var now = time.GetUtcNow().UtcDateTime;
            var claimed = await db.Alerts
                .Where(a => a.Id == alert.AlertId && a.SentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.SentAt, now), ct);
            if (claimed == 0)
            {
                continue;
            }

            try
            {
                await careTeam.SendAlertAsync(alert.NationalId, Domain.Reading.ToMmolPerL(alert.Value, alert.Unit), alert.CollectedAt, ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Care team alert {AlertId} failed; it will be retried", alert.AlertId);
                await db.Alerts.Where(a => a.Id == alert.AlertId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.SentAt, (DateTime?)null), CancellationToken.None);
            }
            catch
            {
                // Shutting down mid-send: unknown whether it arrived, so retry rather than lose a high reading.
                await db.Alerts.Where(a => a.Id == alert.AlertId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.SentAt, (DateTime?)null), CancellationToken.None);
                throw;
            }
        }

        if (sent > 0)
        {
            logger.LogInformation("Sent {Count} care team alerts", sent);
        }
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
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception here would stop the whole host.
                logger.LogError(ex, "Care team alert run failed");
            }
        }
    }
}
