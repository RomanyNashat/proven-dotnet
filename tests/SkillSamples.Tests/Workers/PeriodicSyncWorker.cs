using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkillSamples.Workers;

public interface ISyncJob
{
    Task RunAsync(CancellationToken ct);
}

// Runs once at start, then every interval. One failed run is logged and the next tick runs anyway.
// The TimeProvider makes the interval testable without waiting for it.
public sealed class PeriodicSyncWorker(ISyncJob job, TimeProvider time, ILogger<PeriodicSyncWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await job.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sync failed; next run in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
