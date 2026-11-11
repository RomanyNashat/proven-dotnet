namespace MeterSync.Application;

/// <summary>Every minute: pull new readings, then text the patients whose readings are out of range.</summary>
public sealed class SyncWorker(IServiceScopeFactory scopes, AlertSweeper sweeper, TimeProvider time, ILogger<SyncWorker> logger)
    : BackgroundService
{
    /// <summary>
    /// One run: sync, then sweep, each on its own so a vendor outage doesn't stop alerts. Any failure is logged
    /// and the loop goes on, a timeout's TaskCanceledException included; only the host stopping ends it.
    /// </summary>
    internal static async Task RunIterationAsync(
        Func<CancellationToken, Task> sync, Func<CancellationToken, Task> sweep, ILogger logger, CancellationToken stoppingToken)
    {
        await RunStepAsync("Meter sync", sync, logger, stoppingToken);
        await RunStepAsync("Alert sweep", sweep, logger, stoppingToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        do
        {
            await RunIterationAsync(SyncAsync, sweeper.RunOnceAsync, logger, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static async Task RunStepAsync(string step, Func<CancellationToken, Task> run, ILogger logger, CancellationToken stoppingToken)
    {
        try
        {
            await run(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "{Step} run failed", step);
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MeterSyncService>().SyncOnceAsync(ct);
    }
}
