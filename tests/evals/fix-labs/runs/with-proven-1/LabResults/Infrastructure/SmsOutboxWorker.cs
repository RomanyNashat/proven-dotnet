namespace LabResults.Infrastructure;

/// <summary>Runs <see cref="SmsOutboxRelay"/> on a timer, with a new scope (and DbContext) each run.</summary>
public sealed class SmsOutboxWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<SmsOutboxWorker> logger)
    : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SmsOutboxRelay>().PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Asking the token, not the exception type: an HttpClient or Npgsql timeout is also an
                // OperationCanceledException, and letting it out would stop the host.
                logger.LogError("SMS outbox run failed ({ErrorType}); retrying on the next tick", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
