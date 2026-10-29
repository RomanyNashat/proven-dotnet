namespace LabResults.Application;

/// <summary>
/// Polls the SMS outbox and sends what's pending. A failed run is logged and the next tick runs anyway:
/// an HttpClient timeout is a TaskCanceledException, and letting it escape stops the whole host.
/// </summary>
public sealed class NotificationWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    /// <summary>How long the worker waits when the outbox is empty or a run failed.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                sent = await scope.ServiceProvider.GetRequiredService<ISmsRelay>().RelayPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The type only: the exception text can carry the phone number or the gateway's echo of it.
                logger.LogError("SMS relay run failed with {ExceptionType}; retrying in {Interval}", ex.GetType().Name, PollInterval);
            }

            if (sent == 0)
            {
                try
                {
                    await Task.Delay(PollInterval, time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
