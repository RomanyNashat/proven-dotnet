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

        var waitingIds = await db.Alerts.AsNoTracking().Where(a => a.SentAt == null).OrderBy(a => a.Id).Take(50)
            .Select(a => a.Id).ToListAsync(ct);

        var sent = 0;
        foreach (var id in waitingIds)
        {
            try
            {
                // Row lock, so two instances never send the same alert (the care team can't de-duplicate).
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var alert = await db.Alerts
                    .FromSqlInterpolated($"SELECT * FROM care_team_alerts WHERE \"Id\" = {id} AND \"SentAt\" IS NULL FOR UPDATE SKIP LOCKED")
                    .SingleOrDefaultAsync(ct);
                if (alert is null)
                {
                    continue;
                }

                var reading = await db.Readings.AsNoTracking().SingleAsync(r => r.Id == alert.ReadingId, ct);
                await careTeam.SendAlertAsync(reading.NationalId, reading.ValueMmolL, reading.CollectedAt, ct);
                alert.MarkSent(time.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failing alert must not block the others; it stays unsent and is tried again next run.
                logger.LogError(ex, "Could not send care team alert {AlertId}", id);
                db.ChangeTracker.Clear();
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An uncaught error here would stop the whole host.
                logger.LogError(ex, "Care team alert run failed");
            }
        }
    }
}
