using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClinicQueue.Application;

/// <summary>Fills the clinic directory before the service takes traffic, then refreshes it every ten minutes.</summary>
public sealed class DirectoryWarmer(IServiceScopeFactory scopes, ClinicDirectory directory) : BackgroundService
{
    /// <summary>Loads the directory, then starts the refresh loop.</summary>
    /// <param name="cancellationToken">Cancels start-up.</param>
    /// <returns>A task that completes once the directory is loaded.</returns>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // The first load must finish before the host starts serving. Up to .NET 9 the synchronous part of
        // ExecuteAsync ran inside StartAsync; since .NET 10 ExecuteAsync runs wholly in the background, so
        // the load moved here. A failure still stops start-up, as it did before.
        Refresh();
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        using var scope = scopes.CreateScope();
        directory.Replace(scope.ServiceProvider.GetRequiredService<IClinicSource>().Load());
    }
}
