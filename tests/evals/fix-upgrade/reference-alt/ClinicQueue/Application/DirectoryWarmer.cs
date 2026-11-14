using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClinicQueue.Application;

/// <summary>Refreshes the clinic directory every ten minutes. The first fill is <see cref="DirectoryFirstFill"/>.</summary>
public sealed class DirectoryWarmer(IServiceScopeFactory scopes, ClinicDirectory directory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            DirectoryFirstFill.Fill(scopes, directory);
        }
    }
}

/// <summary>Fills the directory before the host takes traffic: the host awaits every StartAsync, and on
/// .NET 10 a BackgroundService's ExecuteAsync no longer runs before that.</summary>
public sealed class DirectoryFirstFill(IServiceScopeFactory scopes, ClinicDirectory directory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.Run(() => Fill(scopes, directory), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static void Fill(IServiceScopeFactory scopes, ClinicDirectory directory)
    {
        using var scope = scopes.CreateScope();
        directory.Replace(scope.ServiceProvider.GetRequiredService<IClinicSource>().Load());
    }
}
