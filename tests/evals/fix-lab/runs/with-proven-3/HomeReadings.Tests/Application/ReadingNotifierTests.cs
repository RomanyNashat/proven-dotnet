using HomeReadings.Application;
using HomeReadings.Infrastructure;
using HomeReadings.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HomeReadings.Tests.Application;

public sealed class ReadingNotifierTests
{
    [Fact]
    public async Task BackgroundLoop_RunFails_LogsAndKeepsRunning()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ReadingsDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=readings;Username=readings;Password=x;Timeout=1"));
        services.AddHttpClient<CareTeamClient>(c => c.BaseAddress = new Uri("https://careteam.example.test/api/"));
        await using var provider = services.BuildServiceProvider();
        var time = new FakeTimeProvider();
        var logger = new RecordingLogger<ReadingNotifier>();
        var notifier = new ReadingNotifier(provider.GetRequiredService<IServiceScopeFactory>(), time, logger);

        await notifier.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!logger.FirstError.IsCompleted && !notifier.ExecuteTask!.IsCompleted && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            await Task.WhenAny(logger.FirstError, Task.Delay(100));
        }

        notifier.ExecuteTask!.IsCompleted.Should().BeFalse("one failed run must not stop the loop (and with it the host)");
        logger.FirstError.IsCompleted.Should().BeTrue("the failed run is logged");
        await notifier.StopAsync(CancellationToken.None);
    }
}
