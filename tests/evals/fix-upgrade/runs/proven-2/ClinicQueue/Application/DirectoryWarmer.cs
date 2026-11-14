using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>Fills the clinic directory before the service takes traffic, then refreshes it every ten minutes.</summary>
public sealed partial class DirectoryWarmer(IServiceScopeFactory scopes, ClinicDirectory directory, ILogger<DirectoryWarmer> logger)
    : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // On .NET 10 all of ExecuteAsync runs as a Task, so the first fill lives here: the host awaits
        // StartAsync, and the first request already sees every clinic. A failure here still fails start-up.
        Refresh();
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Refresh();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // One failed refresh keeps the last good list; letting it escape would stop the host.
                LogRefreshFailed(logger, ex);
            }
        }
    }

    private void Refresh()
    {
        using var scope = scopes.CreateScope();
        directory.Replace(scope.ServiceProvider.GetRequiredService<IClinicSource>().Load());
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Refreshing the clinic directory failed; keeping the previous list")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
