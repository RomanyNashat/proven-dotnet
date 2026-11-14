using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ClinicQueue.Tests;

public sealed class DirectoryWarmerTests
{
    private static readonly IReadOnlyList<ClinicEntry> Clinics = [new(1, "North")];

    [Fact]
    public async Task StartAsync_SlowSource_DirectoryIsFilledWhenStartReturns()
    {
        var directory = new ClinicDirectory();
        using var provider = Provider(new SlowSource());
        using var warmer = new DirectoryWarmer(
            provider.GetRequiredService<IServiceScopeFactory>(), directory, new FakeTimeProvider(), NullLogger<DirectoryWarmer>.Instance);

        await warmer.StartAsync(CancellationToken.None);

        directory.All.Should().BeEquivalentTo(Clinics);
        await warmer.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider Provider(IClinicSource source)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => source);
        return services.BuildServiceProvider();
    }

    private sealed class SlowSource : IClinicSource
    {
        public IReadOnlyList<ClinicEntry> Load()
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(300));
            return Clinics;
        }
    }
}
