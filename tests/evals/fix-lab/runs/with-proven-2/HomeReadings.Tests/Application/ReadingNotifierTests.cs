using HomeReadings.Application;
using HomeReadings.Infrastructure;
using HomeReadings.Tests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace HomeReadings.Tests.Application;

public sealed class ReadingNotifierTests
{
    [Fact]
    public async Task ExecuteAsync_ARunFails_LogsAndKeepsRunning()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ReadingsDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"));
        services.AddHttpClient<CareTeamClient>(c => c.BaseAddress = new Uri("http://careteam.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => new CareTeamStubHandler(new CareTeamStub()));
        await using var provider = services.BuildServiceProvider();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 6, 0, 0, TimeSpan.Zero));
        var logger = new FakeLogger<ReadingNotifier>();
        var notifier = new ReadingNotifier(provider.GetRequiredService<IServiceScopeFactory>(), time, logger);

        await notifier.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && ErrorCount(logger) < 2 && notifier.ExecuteTask?.IsCompleted != true)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(100);
        }

        notifier.ExecuteTask!.IsCompleted.Should().BeFalse("a failed run must not stop the worker, or StopHost takes the API down");
        ErrorCount(logger).Should().BeGreaterThanOrEqualTo(2);
        await notifier.StopAsync(CancellationToken.None);
    }

    private static int ErrorCount(FakeLogger logger) =>
        logger.Collector.GetSnapshot().Count(r => r.Level == LogLevel.Error);
}
