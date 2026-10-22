using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>
/// Sends waiting alerts to the care team every minute.
/// <para>
/// The care team has no idempotency key, so every POST is a new alert to a nurse. Delivery is at-least-once,
/// with every avoidable duplicate removed: one sender across all pods (an advisory lock), no retried POSTs
/// (see the client's registration), and each alert marked sent the moment its POST succeeds. What remains is
/// a crash between a successful POST and that mark, or a timeout on a POST that did arrive; either sends the
/// alert again on the next run. Losing an alert for a high reading is the worse failure, and the repeat
/// carries the same national ID, value and time.
/// </para>
/// </summary>
public sealed class ReadingNotifier(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReadingNotifier> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private const int BatchSize = 50;

    // Any constant, unique within this service's database.
    private const long SenderLockKey = 7_311_204;

    /// <summary>
    /// Sends up to one batch of waiting alerts, unless another pod is sending. A care team failure is
    /// recorded on the alert and does not throw; the alert goes again on the next run.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
        var careTeam = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

        // A session lock, on a connection held for the whole run, so each alert can still commit on its own.
        // The database releases it if the pod dies.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({SenderLockKey}) AS \"Value\"").SingleAsync(ct))
            {
                logger.LogDebug("Another pod is sending care team alerts");
                return;
            }

            try
            {
                await SendWaitingAsync(db, careTeam, ct);
            }
            finally
            {
                await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({SenderLockKey}) AS \"Value\"").SingleAsync(CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
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
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // A failed run must not stop the host: that would take reading intake down with it. Care team
                // failures never get here (they're recorded per alert); this is the database or a bug.
                logger.LogError(ex, "Sending care team alerts failed; next run in {Interval}", Interval);
            }
        }
    }

    private async Task SendWaitingAsync(ReadingsDbContext db, CareTeamClient careTeam, CancellationToken ct)
    {
        var waiting = await db.Alerts
            .Where(a => a.SentAt == null)
            .OrderBy(a => a.Id)
            .Take(BatchSize)
            .Join(db.Readings, a => a.ReadingId, r => r.Id, (a, r) => new { Alert = a, r.NationalId, r.Value, r.CollectedAt })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var item in waiting)
        {
            try
            {
                await careTeam.SendAlertAsync(item.NationalId, item.Value, item.CollectedAt, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var error = AlertFailure.Describe(ex);
                item.Alert.RecordFailure(error, time.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);

                // The type and status only: a downstream message can echo the national ID.
                logger.LogWarning("Care team alert {AlertId} not sent ({Error}), attempt {Attempts}", item.Alert.Id, error, item.Alert.Attempts);
                if (AlertFailure.IsOnlyThisAlert(ex))
                {
                    continue;
                }

                break;   // the care team is down: try again next run instead of waiting on every alert
            }

            item.Alert.MarkSent(time.GetUtcNow().UtcDateTime);

            // The nurse has the alert: record it even if shutdown has begun, or it goes out twice.
            await db.SaveChangesAsync(CancellationToken.None);
            sent++;
        }

        if (sent > 0)
        {
            logger.LogInformation("Sent {Count} care team alerts", sent);
        }
    }
}
