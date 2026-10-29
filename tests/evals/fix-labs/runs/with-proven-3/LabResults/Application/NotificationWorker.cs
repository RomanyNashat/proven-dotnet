namespace LabResults.Application;

/// <summary>Sends the queued SMS one at a time, so a burst of results doesn't flood the gateway.</summary>
public sealed class NotificationWorker(NotificationQueue queue, IServiceScopeFactory scopes, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Drain before cancelling: base.StopAsync cancels stoppingToken at once, which would drop what is queued.
        // cancellationToken carries the host's shutdown timeout, so this still ends when that runs out.
        queue.Complete();
        if (ExecuteTask is { } running)
        {
            await Task.WhenAny(running, Task.Delay(Timeout.Infinite, cancellationToken));
        }

        await base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISmsSender>().SendAsync(notification.To, notification.Text, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Any failure (a timeout is a TaskCanceledException) is logged and skipped: one escaping here
                // stops the whole host. The type only: the gateway's error text can repeat the phone number.
                logger.LogError("SMS send failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
