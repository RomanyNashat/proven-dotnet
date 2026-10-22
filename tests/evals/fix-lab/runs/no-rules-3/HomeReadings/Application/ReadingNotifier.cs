using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>Sends waiting alerts to the care team every minute.</summary>
public sealed class ReadingNotifier(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReadingNotifier> logger) : BackgroundService
{
    private const int BatchSize = 50;

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var sent = 0;
        var lastId = 0;
        for (var i = 0; i < BatchSize; i++)
        {
            // A fresh scope per alert: a failed one can't poison the context of the next.
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
            var careTeam = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

            // The row lock (skipping rows other instances hold) keeps two instances from sending the same alert,
            // which matters because the care team has no idempotency key: every duplicate is a new alert to a nurse.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var alert = await db.Alerts
                .FromSqlInterpolated($"""SELECT * FROM care_team_alerts WHERE "SentAt" IS NULL AND "Id" > {lastId} ORDER BY "Id" LIMIT 1 FOR UPDATE SKIP LOCKED""")
                .SingleOrDefaultAsync(ct);
            if (alert is null)
            {
                break;
            }

            lastId = alert.Id;
            try
            {
                var reading = await db.Readings.SingleAsync(r => r.Id == alert.ReadingId, ct);
                await careTeam.SendAlertAsync(reading.NationalId, reading.Value, reading.CollectedAt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Left unsent (the transaction rolls back) and retried on the next run; the other alerts still go out.
                logger.LogError(ex, "Could not send care team alert {AlertId}", alert.Id);
                continue;
            }

            alert.MarkSent(time.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            sent++;
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
                // An unhandled exception here would stop the whole host.
                logger.LogError(ex, "Care team alert run failed");
            }
        }
    }
}
