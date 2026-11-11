namespace MeterSync.Application;

/// <summary>Every minute: pull new readings, then text the patients whose readings are out of range.</summary>
public sealed class SyncWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<SyncWorker> logger)
    : BackgroundService
{
    internal static async Task RunStepAsync(string step, Func<CancellationToken, Task> run, ILogger logger, CancellationToken stoppingToken)
    {
        try
        {
            await run(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Includes TaskCanceledException from an HttpClient timeout: that is a failed run,
            // not the host stopping, and letting it escape would fault the worker and stop the host.
            logger.LogError(ex, "{Step} run failed", step);
        }
    }

    /// <summary>One run: sync, then sweep. A failed sync still sweeps what is already stored.</summary>
    internal async Task TickAsync(CancellationToken stoppingToken)
    {
        await RunStepAsync("Meter sync", InScopeAsync<MeterSyncService>((s, ct) => s.SyncOnceAsync(ct)), logger, stoppingToken);
        await RunStepAsync("Alert sweep", InScopeAsync<AlertSweeper>((s, ct) => s.RunOnceAsync(ct)), logger, stoppingToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        do
        {
            await TickAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private Func<CancellationToken, Task> InScopeAsync<TService>(Func<TService, CancellationToken, Task> run)
        where TService : notnull =>
        async ct =>
        {
            await using var scope = scopes.CreateAsyncScope();
            await run(scope.ServiceProvider.GetRequiredService<TService>(), ct);
        };
}
