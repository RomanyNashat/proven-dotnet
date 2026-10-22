using System.Net.Http;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Polly.CircuitBreaker;
using Polly.RateLimiting;

namespace HomeReadings.Application;

/// <summary>Sends waiting alerts to the care team every minute.</summary>
/// <remarks>
/// The care team's system has no idempotency key, so every POST reaches a nurse as a new alert. Each alert
/// is claimed in the database before it is sent, so two pods (or two runs) never send the same one. When a
/// send fails in a way that shows the care team did not take it, the claim is released and the next run
/// tries again. When the outcome is unknown (a timeout, a dropped connection), the claim is kept until
/// <see cref="ClaimTimeout"/> passes: a high reading is sent again rather than lost, but not every minute.
/// </remarks>
public sealed class ReadingNotifier(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReadingNotifier> logger) : BackgroundService
{
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(15);

    private const int BatchSize = 50;

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>();
        var careTeam = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

        var staleBefore = time.GetUtcNow().UtcDateTime - ClaimTimeout;
        var waiting = await db.Alerts.AsNoTracking()
            .Where(a => a.SentAt == null && (a.ClaimedAt == null || a.ClaimedAt < staleBefore))
            .OrderBy(a => a.Id)
            .Take(BatchSize)
            .Select(a => new { a.Id, Reading = a.Reading! })
            .ToListAsync(ct);

        var sent = 0;
        var failed = 0;
        foreach (var alert in waiting)
        {
            DateTime? claimedAt = time.GetUtcNow().UtcDateTime;
            var claimed = await db.Alerts
                .Where(a => a.Id == alert.Id && a.SentAt == null && (a.ClaimedAt == null || a.ClaimedAt < staleBefore))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ClaimedAt, claimedAt), ct);
            if (claimed == 0)
            {
                continue; // another notifier has it, or has sent it
            }

            try
            {
                var reading = alert.Reading;
                await careTeam.SendAlertAsync(reading.NationalId, reading.ValueInMmolPerLitre, reading.CollectedAt, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && WasNotDelivered(ex))
            {
                failed++;
                logger.LogWarning(ex, "Care team did not take alert {AlertId}; it will be sent on the next run", alert.Id);
                await db.Alerts.Where(a => a.Id == alert.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ClaimedAt, (DateTime?)null), ct);
                continue;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failed++;
                logger.LogError(ex, "Sending alert {AlertId} ended without an answer from the care team; it is sent again after {ClaimTimeout} unless marked sent",
                    alert.Id, ClaimTimeout);
                continue;
            }

            // The nurse has the alert: record it even if we are shutting down, or it would be sent again.
            DateTime? sentAt = time.GetUtcNow().UtcDateTime;
            await db.Alerts.Where(a => a.Id == alert.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.SentAt, sentAt), CancellationToken.None);
            sent++;
        }

        if (sent > 0 || failed > 0)
        {
            logger.LogInformation("Sent {Sent} care team alerts, {Failed} failed", sent, failed);
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // An unhandled exception here would stop the whole host (BackgroundServiceExceptionBehavior.StopHost).
                logger.LogError(ex, "Sending care team alerts failed; trying again on the next run");
            }
        }
    }

    /// <summary>True when the failure shows the care team cannot have created the alert.</summary>
    private static bool WasNotDelivered(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: not null } => true, // the care team answered with an error
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError } => true,
        BrokenCircuitException or RateLimiterRejectedException => true, // never left this service
        _ => false,
    };
}
