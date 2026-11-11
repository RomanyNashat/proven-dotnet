namespace MeterSync.Application;

/// <summary>Every minute: pull new readings, then text the patients whose readings are out of range.</summary>
public sealed class SyncWorker(IServiceScopeFactory scopes, AlertSweeper sweeper, TimeProvider time, ILogger<SyncWorker> logger)
    : BackgroundService
{
    /// <summary>Runs one step of the loop; a failure is logged so the worker survives to the next tick.</summary>
    /// <param name="step">The step to run.</param>
    /// <param name="name">The step's name, for the log.</param>
    /// <param name="logger">Where failures go.</param>
    /// <param name="stoppingToken">The host's stopping token; only its cancellation ends the loop.</param>
    /// <returns>The step.</returns>
    internal static async Task RunStepAsync(Func<CancellationToken, Task> step, string name, ILogger logger, CancellationToken stoppingToken)
    {
        try
        {
            await step(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // An HttpClient timeout is a TaskCanceledException too; only a stopping host may end the worker
            // (an unhandled exception here stops the host, and with it the pod).
            logger.LogError(ex, "Meter sync step {Step} failed", name);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        do
        {
            await RunStepAsync(SyncAsync, "sync", logger, stoppingToken);

            // Separate from the sync, so a failed sync doesn't hold back alerts already in the database.
            await RunStepAsync(sweeper.RunOnceAsync, "alerts", logger, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MeterSyncService>().SyncOnceAsync(ct);
    }
}
