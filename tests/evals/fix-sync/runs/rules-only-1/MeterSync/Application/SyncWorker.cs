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
            // Separate steps: a failed sync must not hold back alerts for readings already stored.
            await RunStepAsync("Meter sync", sp => sp.GetRequiredService<MeterSyncService>().SyncOnceAsync(stoppingToken), stoppingToken);
            await RunStepAsync("Alert sweep", sp => sp.GetRequiredService<AlertSweeper>().RunOnceAsync(stoppingToken), stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunStepAsync(string name, Func<IServiceProvider, Task<int>> step, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await step(scope.ServiceProvider);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // An HttpClient timeout is an OperationCanceledException too. Letting it escape would end the
            // worker, and by default the host with it.
            logger.LogError(ex, "{Step} run failed", name);
        }
    }
}
