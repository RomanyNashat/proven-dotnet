using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>Fills the clinic directory before the service takes traffic, then refreshes it every ten minutes.</summary>
public sealed partial class DirectoryWarmer(IServiceScopeFactory scopes, ClinicDirectory directory, ILogger<DirectoryWarmer> logger)
    : BackgroundService
{
    private static readonly TimeSpan RefreshPeriod = TimeSpan.FromMinutes(10);

    /// <summary>Loads the directory, then starts the periodic refresh.</summary>
    /// <param name="cancellationToken">Cancels start-up.</param>
    /// <returns>Completes once the directory is filled.</returns>
    /// <remarks>
    /// The first load is here, not in <see cref="ExecuteAsync"/>: since .NET 10 the host runs all of
    /// ExecuteAsync in the background, so the server could take requests while the directory is still empty.
    /// A failure here stops start-up, as it did on .NET 8, rather than serving an empty clinic list.
    /// </remarks>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Refresh();
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshPeriod);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Refresh();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep serving the last good list: a database blip must not stop the host.
                LogRefreshFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Refreshing the clinic directory failed; the previous list stays in use.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    private void Refresh()
    {
        using var scope = scopes.CreateScope();
        directory.Replace(scope.ServiceProvider.GetRequiredService<IClinicSource>().Load());
    }
}
