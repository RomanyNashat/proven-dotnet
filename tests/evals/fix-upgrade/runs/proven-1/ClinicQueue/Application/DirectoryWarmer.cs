using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>Fills the clinic directory before the service takes traffic, then refreshes it every ten minutes.</summary>
public sealed partial class DirectoryWarmer(
    IServiceScopeFactory scopes, ClinicDirectory directory, TimeProvider time, ILogger<DirectoryWarmer> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The first fill happens here, which the host awaits, so the first request already sees every clinic. It
    /// was at the top of ExecuteAsync on .NET 8; from .NET 10 all of ExecuteAsync runs as a Task and no longer
    /// holds up start-up. A failure here still stops the service from starting, as before.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Refresh();
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshEvery, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Refresh();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed refresh keeps the list it has and tries again next tick, rather than stopping the host.
                LogRefreshFailed(logger, ex);
            }
        }
    }

    private void Refresh()
    {
        using var scope = scopes.CreateScope();
        directory.Replace(scope.ServiceProvider.GetRequiredService<IClinicSource>().Load());
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Refreshing the clinic directory failed; keeping the current list")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
