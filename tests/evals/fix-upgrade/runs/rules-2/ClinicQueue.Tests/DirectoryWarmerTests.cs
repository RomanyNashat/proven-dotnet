using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicQueue.Tests;

public sealed class DirectoryWarmerTests
{
    [Fact]
    public async Task StartAsync_SourceHasClinics_DirectoryFilledBeforeStartReturns()
    {
        var services = new ServiceCollection();
        services.AddScoped<IClinicSource>(_ => new SlowClinicSource([new ClinicEntry(1, "North")]));
        await using var provider = services.BuildServiceProvider();
        var directory = new ClinicDirectory();
        using var warmer = new DirectoryWarmer(provider.GetRequiredService<IServiceScopeFactory>(), directory);

        await warmer.StartAsync(CancellationToken.None);

        directory.All.Should().ContainSingle().Which.Should().Be(new ClinicEntry(1, "North"));
        await warmer.StopAsync(CancellationToken.None);
    }

    private sealed class SlowClinicSource(IReadOnlyList<ClinicEntry> clinics) : IClinicSource
    {
        public IReadOnlyList<ClinicEntry> Load()
        {
            // Stands in for a database round trip, so a load that runs in the background loses the race.
            using var never = new ManualResetEventSlim(false);
            never.Wait(TimeSpan.FromMilliseconds(300));
            return clinics;
        }
    }
}
