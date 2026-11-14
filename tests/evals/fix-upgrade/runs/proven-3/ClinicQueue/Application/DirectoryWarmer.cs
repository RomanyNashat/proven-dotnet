using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>Fills the clinic directory before the service takes traffic, then refreshes it every ten minutes.</summary>
public sealed partial class DirectoryWarmer(IServiceScopeFactory scopes, ClinicDirectory directory, ILogger<DirectoryWarmer> logger)
    : BackgroundService
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Fills the directory first. The host awaits StartAsync, so the first request sees every clinic. On .NET 8
    /// this was the code before ExecuteAsync's first await; .NET 10 runs all of ExecuteAsync in the background.
    /// </summary>
    /// <param name="cancellationToken">Cancels start-up.</param>
    /// <returns>A task that completes once the directory is filled.</returns>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Refresh();
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshEvery);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Refresh();
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed refresh keeps the last good list; letting it escape would stop the whole host.
                LogRefreshFailed(logger, exception);
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
