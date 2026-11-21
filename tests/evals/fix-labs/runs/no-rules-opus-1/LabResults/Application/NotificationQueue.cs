using LabResults.Domain;
using LabResults.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Application;

/// <summary>Wakes the worker when a new SMS has been committed to the outbox, instead of waiting for the next poll.</summary>
public sealed class NotificationSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Sends the SMS in the outbox one at a time, so a burst of results doesn't flood the gateway.
/// Delivery is at-least-once: a crash between the gateway accepting a message and the delete committing resends it.
/// </summary>
public sealed class NotificationWorker(
    NotificationSignal signal, IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool sentOne;
            try
            {
                sentOne = await SendNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let the worker die (that would stop the host): the database may be briefly unavailable.
                logger.LogError(ex, "SMS outbox processing failed");
                sentOne = false;
            }

            if (!sentOne)
            {
                try
                {
                    await signal.WaitAsync(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <returns><c>true</c> when a message was processed, so there may be more.</returns>
    private async Task<bool> SendNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();
        var now = time.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED lets several instances of the service share the outbox without sending a message twice.
        var message = await db.SmsOutbox
            .FromSql($"""SELECT * FROM sms_outbox WHERE "NextAttemptAt" <= {now} ORDER BY "Id" LIMIT 1 FOR UPDATE SKIP LOCKED""")
            .SingleOrDefaultAsync(ct);
        if (message is null)
        {
            return false;
        }

        try
        {
            await sms.SendAsync(message.To, message.Text, ct);
            db.SmsOutbox.Remove(message);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            if (message.Failed(time.GetUtcNow()))
            {
                logger.LogWarning(ex, "SMS {MessageId} to {To} failed (attempt {Attempt}); will retry", message.Id, Masking.Identifier(message.To), message.Attempts);
            }
            else
            {
                logger.LogError(ex, "SMS {MessageId} to {To} failed {Attempts} times; giving up", message.Id, Masking.Identifier(message.To), message.Attempts);
                db.SmsOutbox.Remove(message);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
        return true;
    }
}
