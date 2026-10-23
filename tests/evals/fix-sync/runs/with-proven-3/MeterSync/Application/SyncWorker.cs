namespace MeterSync.Application;

/// <summary>Every minute: pull new readings, then text the patients whose readings are out of range.</summary>
public sealed class SyncWorker(IServiceScopeFactory scopes, AlertSweeper sweeper, TimeProvider time, ILogger<SyncWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        do
        {
            // Separate steps: a vendor outage must not stop the alerts that are already stored.
            await RunStepAsync(
                async ct =>
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<MeterSyncService>().SyncOnceAsync(ct);
                },
                "Meter sync",
                logger,
                stoppingToken);

            await RunStepAsync(async ct => await sweeper.RunOnceAsync(ct), "Alert sweep", logger, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Runs one step and keeps the worker alive when it fails. Only the host stopping ends the loop: an
    /// HttpClient timeout is a TaskCanceledException too, and filtering on the exception type let it escape,
    /// fault ExecuteAsync and stop the host.
    /// </summary>
    internal static async Task RunStepAsync(Func<CancellationToken, Task> step, string name, ILogger logger, CancellationToken stoppingToken)
    {
        try
        {
            await step(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "{Step} run failed", name);
        }
    }
}
