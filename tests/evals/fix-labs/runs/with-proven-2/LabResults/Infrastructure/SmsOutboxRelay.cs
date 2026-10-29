using LabResults.Application;
using LabResults.Domain;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

/// <summary>
/// Sends pending outbox SMS, one relay at a time across pods (a transaction-scoped advisory lock), so two
/// pods don't text the same patient twice. SMS order doesn't matter, so a failed message is counted and the
/// batch carries on; after <see cref="SmsOutboxMessage.MaxAttempts"/> it stays for an operator.
/// </summary>
public sealed class SmsOutboxRelay(LabDbContext db, ISmsSender sms, ILogger<SmsOutboxRelay> logger) : ISmsRelay
{
    private const int BatchSize = 20;
    private const long RelayLockKey = 7_410_201;   // unique per service database

    /// <inheritdoc />
    public async Task<int> RelayPendingAsync(CancellationToken ct)
    {
        // No EnableRetryOnFailure on this context: a retried unit would re-send the HTTP calls inside it.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({RelayLockKey}) AS \"Value\"")
            .SingleAsync(ct);
        if (!locked)
        {
            return 0;   // another pod is relaying
        }

        var batch = await db.SmsOutbox
            .Where(m => m.Attempts < SmsOutboxMessage.MaxAttempts)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var message in batch)
        {
            try
            {
                await sms.SendAsync(message.Phone, SmsTemplates.ResultReady, ct);
                db.SmsOutbox.Remove(message);
                sent++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                message.RecordFailure(ex.GetType().Name);
                logger.LogWarning("SMS {OutboxId} failed with {ExceptionType} (attempt {Attempt})", message.Id, ex.GetType().Name, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return sent;
    }
}
