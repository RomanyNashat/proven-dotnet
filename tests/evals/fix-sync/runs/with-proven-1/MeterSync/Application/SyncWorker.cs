namespace MeterSync.Application;

/// <summary>Every minute: pull new readings, then text the patients whose readings are out of range.</summary>
public sealed class SyncWorker(IServiceScopeFactory scopes, AlertSweeper sweeper, TimeProvider time, ILogger<SyncWorker> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            // Two steps, two tries: a vendor outage must not stop alerts for readings already stored.
            await RunAsync("Meter sync", async ct =>
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MeterSyncService>().SyncOnceAsync(ct);
            }, stoppingToken);
            await RunAsync("Alert sweep", sweeper.RunOnceAsync, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(string step, Func<CancellationToken, Task> run, CancellationToken stoppingToken)
    {
        try
        {
            await run(stoppingToken);
        }
        // Not `ex is not OperationCanceledException`: an HTTP timeout is a TaskCanceledException, and letting
        // it out faults the worker, which stops the host. Only shutdown ends the loop.
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // The type and stack trace, not the message, which can carry a URL with a serial or a value.
            logger.LogError("{Step} failed: {ExceptionType}\n{StackTrace}", step, ex.GetType().FullName, ex.StackTrace);
        }
    }
}
