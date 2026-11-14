using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicQueue.Tests;

public sealed class DirectoryWarmerTests
{
    [Fact]
    public async Task StartAsync_SlowSource_DirectoryFilledBeforeStartReturns()
    {
        var services = new ServiceCollection();
        services.AddScoped<IClinicSource>(_ => new SlowClinicSource([new ClinicEntry(1, "North")]));
        await using var provider = services.BuildServiceProvider();
        var directory = new ClinicDirectory();
        using var warmer = new DirectoryWarmer(
            provider.GetRequiredService<IServiceScopeFactory>(), directory, NullLogger<DirectoryWarmer>.Instance);

        await warmer.StartAsync(CancellationToken.None);

        directory.All.Should().Equal(new ClinicEntry(1, "North"));
        await warmer.StopAsync(CancellationToken.None);
    }

    private sealed class SlowClinicSource(IReadOnlyList<ClinicEntry> clinics) : IClinicSource
    {
        // Load is synchronous by contract; the wait stands in for a slow database round trip.
        public IReadOnlyList<ClinicEntry> Load()
        {
            using var gate = new ManualResetEventSlim();
            gate.Wait(TimeSpan.FromMilliseconds(300));
            return clinics;
        }
    }
}
