using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Every pod runs this. Each alert is claimed for <see cref="ClaimLease"/> before it is sent, so two pods
/// never text the same reading (the gateway has no idempotency key). A failed send releases its claim and
/// is tried again next sweep; a pod that dies mid-send leaves a claim that simply expires.
/// </remarks>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AlertSweeper> logger)
{
    // Sweeps run every minute, so this is about ten minutes of a failing gateway before a person is needed.
    public const int MaxAttempts = 10;
    public const int BatchSize = 100;
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    /// <summary>Sends the alerts due now. Returns how many were sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAlertStore>();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsSender>();

        var sent = 0;
        foreach (var id in await store.DueAsync(time.GetUtcNow(), BatchSize, ct))
        {
            var now = time.GetUtcNow();
            if (!await store.TryClaimAsync(id, now, now + ClaimLease, ct))
            {
                continue;   // another pod has it
            }

            if (await store.LoadAsync(id, ct) is not { } due)
            {
                continue;
            }

            if (await TrySendAsync(sms, due, ct))
            {
                due.Reading.MarkAlertSent(time.GetUtcNow());
                sent++;
            }

            await store.SaveAsync(ct);
        }

        return sent;
    }

    private async Task<bool> TrySendAsync(ISmsSender sms, DueAlert due, CancellationToken ct)
    {
        // One failing alert must not hold up the ones behind it. A timeout is a TaskCanceledException
        // too: only a cancelled sweep is let through.
        try
        {
            if (string.IsNullOrWhiteSpace(due.Phone))
            {
                throw new InvalidOperationException("The patient has no phone number.");
            }

            await sms.SendAsync(due.Phone, AlertText.For(due.Reading), ct);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var gaveUp = due.Reading.RecordAlertFailure(MaxAttempts);
            var level = gaveUp ? LogLevel.Error : LogLevel.Warning;
            logger.Log(level, "Alert for reading {ReadingId} failed (attempt {Attempt}, gave up: {GaveUp}): {ExceptionType} {StatusCode}",
                due.Reading.Id, due.Reading.AlertAttempts, gaveUp, ex.GetType().Name, (ex as HttpRequestException)?.StatusCode);
            return false;
        }
    }
}
