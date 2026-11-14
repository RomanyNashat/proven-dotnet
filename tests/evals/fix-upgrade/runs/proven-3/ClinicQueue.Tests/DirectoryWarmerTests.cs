using ClinicQueue.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicQueue.Tests;

public sealed class DirectoryWarmerTests
{
    [Fact]
    public async Task StartAsync_SlowSource_DirectoryFilledWhenStartReturns()
    {
        var source = new SlowClinicSource(TimeSpan.FromMilliseconds(300), [new ClinicEntry(1, "North")]);
        await using var services = new ServiceCollection().AddScoped<IClinicSource>(_ => source).BuildServiceProvider();
        var directory = new ClinicDirectory();
        using var warmer = new DirectoryWarmer(services.GetRequiredService<IServiceScopeFactory>(), directory, NullLogger<DirectoryWarmer>.Instance);

        await warmer.StartAsync(CancellationToken.None);

        directory.All.Should().ContainSingle().Which.Should().Be(new ClinicEntry(1, "North"));
        await warmer.StopAsync(CancellationToken.None);
    }

    private sealed class SlowClinicSource(TimeSpan delay, IReadOnlyList<ClinicEntry> clinics) : IClinicSource
    {
        private readonly ManualResetEventSlim _never = new(false);

        public IReadOnlyList<ClinicEntry> Load()
        {
            // Blocks like a sync database read would, without Thread.Sleep.
            _never.Wait(delay);
            return clinics;
        }
    }
}
