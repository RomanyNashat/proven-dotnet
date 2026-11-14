using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClinicQueue.Application;

/// <summary>Fills the clinic directory before the service takes traffic, then refreshes it every ten minutes.</summary>
public sealed class DirectoryWarmer(IServiceScopeFactory scopes, ClinicDirectory directory) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // From .NET 10 the host runs all of ExecuteAsync on a background thread and doesn't wait for it,
        // so the first load lives here: the server only starts once every hosted service's StartAsync is
        // done, and the first request already sees every clinic.
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
