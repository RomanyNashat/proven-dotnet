using System.Data.Common;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeterSync.Tests.Application;

public sealed class SyncWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_SyncTimesOut_StillSweepsAndKeepsRunning()
    {
        // Every connection open throws what an HttpClient/command timeout throws: a TaskCanceledException
        // while the host's token is not cancelled.
        var timeouts = new TimingOutConnection(expectedOpens: 2);
        await using var services = BuildServices(timeouts);
        var worker = new SyncWorker(
            services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<SyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var bothRan = await Task.WhenAny(timeouts.ReachedExpected, Task.Delay(TimeSpan.FromSeconds(10))) == timeouts.ReachedExpected;
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        var faulted = worker.ExecuteTask!.IsFaulted;
        await worker.StopAsync(CancellationToken.None);

        bothRan.Should().BeTrue("the sweep runs even when the sync failed");
        faulted.Should().BeFalse("a timeout must not stop the worker (and with it the host)");
    }

    [Fact]
    public async Task StopAsync_DuringRun_CompletesWithoutFault()
    {
        var timeouts = new TimingOutConnection(expectedOpens: 1);
        await using var services = BuildServices(timeouts);
        var worker = new SyncWorker(
            services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<SyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await timeouts.ReachedExpected.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    private static ServiceProvider BuildServices(TimingOutConnection interceptor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<SyncDbContext>(o => o.UseNpgsql("Host=x").AddInterceptors(interceptor));
        services.AddHttpClient<MeterVendorClient>(h => h.BaseAddress = new Uri("http://vendor.test/"));
        services.AddHttpClient<SmsClient>(h => h.BaseAddress = new Uri("http://sms.test/"));
        services.AddScoped<MeterSyncService>();
        services.AddScoped<AlertSweeper>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class TimingOutConnection(int expectedOpens) : DbConnectionInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _opens;

        public Task ReachedExpected => _reached.Task;

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _opens) >= expectedOpens)
            {
                _reached.TrySetResult();
            }

            throw new TaskCanceledException("simulated timeout");
        }
    }
}
