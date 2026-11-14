using Microsoft.Extensions.Hosting;

namespace SkillSamples.Upgrade;

/// <summary>Work the rest of the service needs before it takes traffic, such as a cache filled from the database.</summary>
public interface IWarmUp
{
    Task RunAsync(CancellationToken ct);
}

/// <summary>
/// On .NET 10 all of ExecuteAsync runs as a Task, so the code before its first await no longer runs before
/// the host has started. Work that must be done first goes in StartAsync, which the host awaits; the loop
/// stays in ExecuteAsync.
/// </summary>
public sealed class WarmUpWorker(IWarmUp warmUp) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await warmUp.RunAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
