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
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<MeterSyncService>().SyncOnceAsync(stoppingToken);
                }

                await using (var scope = scopes.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<AlertSweeper>().RunOnceAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An HttpClient timeout is an OperationCanceledException too; letting it escape would stop the host.
                logger.LogError(ex, "Meter sync run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
