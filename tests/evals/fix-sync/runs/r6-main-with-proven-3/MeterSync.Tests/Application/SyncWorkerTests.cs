using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeterSync.Tests.Application;

public sealed class SyncWorkerTests
{
    [Fact]
    public async Task RunStepAsync_DependencyTimesOut_LogsErrorAndReturns()
    {
        var logger = new ListLogger<SyncWorker>();

        // An HttpClient timeout surfaces as TaskCanceledException while the host is still running.
        var act = () => SyncWorker.RunStepAsync("Meter sync", _ => throw new TaskCanceledException(), logger, CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception is TaskCanceledException);
    }

    [Fact]
    public async Task RunStepAsync_HostStopping_LetsCancellationThrough()
    {
        var logger = new ListLogger<SyncWorker>();
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var act = () => SyncWorker.RunStepAsync("Meter sync", ct => Task.FromCanceled(ct), logger, stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task TickAsync_SyncFails_StillRunsSweepFromItsOwnScope()
    {
        var logger = new ListLogger<SyncWorker>();
        await using var services = Services();
        var worker = new SyncWorker(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, logger);

        await worker.TickAsync(CancellationToken.None);

        logger.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message)
            .Should().HaveCount(2).And.Contain(m => m.Contains("sync")).And.Contain(m => m.Contains("sweep"));
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Nothing listens on port 1, so every database call fails at once.
        services.AddDbContext<SyncDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Timeout=2"));
        services.AddHttpClient<MeterVendorClient>(h => h.BaseAddress = new Uri("http://127.0.0.1:1/"));
        services.AddHttpClient<SmsClient>(h => h.BaseAddress = new Uri("http://127.0.0.1:1/"));
        services.AddScoped<MeterSyncService>();
        services.AddScoped<AlertSweeper>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
