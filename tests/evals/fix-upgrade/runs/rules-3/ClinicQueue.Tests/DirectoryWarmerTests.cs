using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicQueue.Tests;

public sealed class DirectoryWarmerTests
{
    [Fact]
    public async Task StartAsync_SourceHasClinics_DirectoryIsFilledBeforeItReturns()
    {
        var directory = new ClinicDirectory();
        var services = new ServiceCollection()
            .AddScoped<IClinicSource, FixedClinicSource>()
            .BuildServiceProvider();
        using var warmer = new DirectoryWarmer(services.GetRequiredService<IServiceScopeFactory>(), directory);

        await warmer.StartAsync(CancellationToken.None);

        directory.All.Should().BeEquivalentTo(FixedClinicSource.Clinics);
        await warmer.StopAsync(CancellationToken.None);
    }
}
