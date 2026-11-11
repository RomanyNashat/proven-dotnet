namespace MeterSync.Application;

/// <summary>Every minute: pull new readings, then text the patients whose readings are out of range.</summary>
public sealed class SyncWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<SyncWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        do
        {
            // Separate steps: a vendor outage must not hold back alerts already stored.
            await RunStepAsync<MeterSyncService>("Meter sync", s => s.SyncOnceAsync(stoppingToken), stoppingToken);
            await RunStepAsync<AlertSweeper>("Alert sweep", s => s.RunOnceAsync(stoppingToken), stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunStepAsync<TStep>(string name, Func<TStep, Task<int>> step, CancellationToken stoppingToken)
        where TStep : notnull
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await step(scope.ServiceProvider.GetRequiredService<TStep>());
        }
        // An HttpClient timeout is an OperationCanceledException too; only shutdown may end the loop, or the
        // host would stop the whole app on the first timeout.
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "{Step} run failed", name);
        }
    }
}
