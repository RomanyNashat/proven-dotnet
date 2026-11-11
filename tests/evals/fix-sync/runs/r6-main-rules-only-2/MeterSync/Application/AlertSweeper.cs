using System.Net;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Several pods run this at once and the gateway has no idempotency key, so each reading is claimed with a
/// conditional UPDATE before it is sent: only the pod whose UPDATE matched sends it. The claim is a lease, so a
/// pod that dies mid-send doesn't strand the alert; it is retried once the lease runs out.
/// </remarks>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    private const int BatchSize = 100;

    // Must outlast the SMS client's timeout, or a slow send could be claimed and sent again by another pod.
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    /// <summary>Sends the pending alerts this instance manages to claim.</summary>
    /// <returns>The number of alerts sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var now = time.GetUtcNow();
        var candidates = await db.Readings.AsNoTracking()
            .Where(r => r.AlertState == AlertState.Pending && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .OrderBy(r => r.Id)
            .Take(BatchSize)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var id in candidates)
        {
            now = time.GetUtcNow();
            var claimed = await db.Readings
                .Where(r => r.Id == id && r.AlertState == AlertState.Pending && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, now + ClaimLease), ct);
            if (claimed == 0)
            {
                continue; // Another pod has it.
            }

            var outcome = await SendAsync(db, sms, id, ct);
            if (outcome == Outcome.Sent)
            {
                sent++;
            }
            else if (outcome == Outcome.GatewayDown)
            {
                break; // Don't wait out a timeout for each of the remaining readings; the next run retries.
            }
        }

        return sent;
    }

    private async Task<Outcome> SendAsync(SyncDbContext db, SmsClient sms, int readingId, CancellationToken ct)
    {
        var item = await db.Readings.AsNoTracking()
            .Where(r => r.Id == readingId)
            .Join(db.Patients, r => r.PatientId, p => p.Id, (r, p) => new { Reading = r, p.Phone })
            .SingleAsync(ct);

        try
        {
            await sms.SendAsync(item.Phone, AlertText.For(item.Reading), ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } status && IsPermanent(status))
        {
            // Retrying a rejected message would block the queue forever; record it for the care team instead.
            logger.LogError(ex, "The SMS gateway rejected the alert for reading {ReadingId} ({Status})", readingId, (int)status);
            await SetStateAsync(db, readingId, AlertState.Rejected, sentAt: null, CancellationToken.None);
            return Outcome.Rejected;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Could not send the alert for reading {ReadingId}; will retry", readingId);
            await db.Readings.Where(r => r.Id == readingId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null), CancellationToken.None);
            return Outcome.GatewayDown;
        }

        // The SMS is out: record it even if we're being stopped, or the next run texts the patient again.
        await SetStateAsync(db, readingId, AlertState.Sent, time.GetUtcNow(), CancellationToken.None);
        return Outcome.Sent;
    }

    private static bool IsPermanent(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && status is not HttpStatusCode.TooManyRequests and not HttpStatusCode.RequestTimeout;

    private static Task<int> SetStateAsync(SyncDbContext db, int readingId, AlertState state, DateTimeOffset? sentAt, CancellationToken ct) =>
        db.Readings.Where(r => r.Id == readingId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.AlertState, state)
                    .SetProperty(r => r.AlertSentAt, sentAt)
                    .SetProperty(r => r.AlertClaimedUntil, (DateTimeOffset?)null),
                ct);

    private enum Outcome
    {
        Sent,
        Rejected,
        GatewayDown,
    }
}
