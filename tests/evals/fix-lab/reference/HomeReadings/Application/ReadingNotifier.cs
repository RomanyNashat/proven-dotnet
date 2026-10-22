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

        var waiting = await db.Alerts.Where(a => a.SentAt == null).OrderBy(a => a.Id).Take(50).ToListAsync(ct);
        var sent = 0;
        foreach (var alert in waiting)
        {
            // One alert the care team rejects must not hold back the others; it stays waiting.
            try
            {
                var reading = await db.Readings.SingleAsync(r => r.Id == alert.ReadingId, ct);
                await careTeam.SendAlertAsync(reading.NationalId, reading.Value, reading.CollectedAt, ct);
                alert.MarkSent(time.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The type only: an HTTP error's message can repeat the request.
                logger.LogWarning("Care team alert {AlertId} not sent: {Error}", alert.Id, ex.GetType().Name);
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
                logger.LogError("Care team run failed: {Error}", ex.GetType().Name);
            }
        }
    }
}
