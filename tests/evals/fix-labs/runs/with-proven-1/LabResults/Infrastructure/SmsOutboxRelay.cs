using LabResults.Application;
using LabResults.Domain;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

/// <summary>
/// Sends the pending SMS from the outbox. One relay at a time across pods (a transaction-scoped advisory
/// lock), so a message isn't sent twice by two pods. Delivery is at-least-once: a gateway timeout after it
/// accepted the message means the patient can get the generic text twice, which is harmless.
/// </summary>
public sealed class SmsOutboxRelay(LabDbContext db, SmsClient sms, TimeProvider time, ILogger<SmsOutboxRelay> logger)
{
    public const int BatchSize = 10;

    // Any constant, unique per service database.
    private const long RelayLockKey = 7_420_001;

    /// <summary>Sends up to <see cref="BatchSize"/> pending messages.</summary>
    /// <param name="ct">Stops the run at shutdown.</param>
    /// <returns>How many were sent.</returns>
    public async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({RelayLockKey}) AS \"Value\"")
            .SingleAsync(ct);
        if (!locked)
        {
            return 0;
        }

        var batch = await db.PendingSms
            .Where(m => m.SentAt == null && m.Attempts < PendingSms.MaxAttempts)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var message in batch)
        {
            if (await TrySendAsync(message, ct))
            {
                sent++;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return sent;
    }

    /// <summary>Sends one message and records the outcome on it; only shutdown escapes as an exception.</summary>
    internal async Task<bool> TrySendAsync(PendingSms message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (message.Phone is null)
        {
            return false;
        }

        try
        {
            await sms.SendAsync(message.Phone, SmsText.ResultReady, ct);
            message.MarkSent(time.GetUtcNow());
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The type and status only: the exception text can carry the phone number.
            message.MarkFailed(ex.GetType().Name);
            logger.LogWarning(
                "SMS {SmsId} failed ({ErrorType}, status {StatusCode}), attempt {Attempt} of {MaxAttempts}",
                message.Id, ex.GetType().Name, (ex as HttpRequestException)?.StatusCode is { } status ? (int)status : null,
                message.Attempts, PendingSms.MaxAttempts);
            return false;
        }
    }
}
