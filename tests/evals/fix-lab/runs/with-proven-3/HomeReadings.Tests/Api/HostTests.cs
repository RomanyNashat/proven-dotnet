using HomeReadings.Application;
using HomeReadings.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeReadings.Tests.Api;

public sealed class HostTests
{
    /// <summary>
    /// Development turns on ValidateScopes and ValidateOnBuild, so building the host there checks every
    /// registration and catches a scoped service captured by a singleton (ICurrentPatient in the notifier
    /// would hand one patient's id to everyone). Production skips both, so this test is where it's checked.
    /// </summary>
    [Fact]
    public void Build_InDevelopment_EveryRegistrationResolvesWithoutCaptiveScopes()
    {
        using var factory = new ServiceFactory().WithWebHostBuilder(b => b.UseEnvironment(Environments.Development));

        factory.Services.GetRequiredService<IHostEnvironment>().IsDevelopment().Should().BeTrue();
        var notifier = factory.Services.GetRequiredService<ReadingNotifier>();

        notifier.Should().BeSameAs(factory.Services.GetServices<IHostedService>().OfType<ReadingNotifier>().Single());
    }
}
