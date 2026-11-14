using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClinicQueue.Application;

/// <summary>Fills the clinic directory before the service takes traffic, then refreshes it every ten minutes.</summary>
public sealed class DirectoryWarmer(IServiceScopeFactory scopes, ClinicDirectory directory) : BackgroundService
{
    // The host awaits StartAsync before it takes traffic. Since .NET 10 it no longer runs ExecuteAsync up to
    // its first await first, so the first fill belongs here.
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
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
