using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SkillSamples.Api;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Upgrade;

// What changes when a service moves to .NET 10, on the runtime these tests run on (10).
public sealed class UpgradeBehaviourTests
{
    // An exception handler written for .NET 8: it answers, and leaves logging to the middleware.
    private sealed class QuietHandler : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
        {
            http.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await http.Response.WriteAsync("error", ct);
            return true;
        }
    }

    private static async Task<(WebApplication App, FakeLogCollector Logs)> StartAsync<THandler>()
        where THandler : class, IExceptionHandler
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddFakeLogging();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<THandler>();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("the database is down")));
        await app.StartAsync();
        return (app, app.Services.GetRequiredService<FakeLogCollector>());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task HandledException_OnDotNet10_LeavesNoErrorInTheLogsUnlessTheHandlerWritesIt()
    {
        // Given a service moved to .NET 10 whose exception handler returns true and doesn't log,
        // because on 8 the middleware logged the exception itself
        var (quiet, quietLogs) = await StartAsync<QuietHandler>();
        await using (quiet)
        {
            // When a request fails
            using var response = await quiet.GetTestClient().GetAsync("/boom");

            // Then the caller gets the error, and the logs (and so Kibana and APM) get nothing
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain(quietLogs.GetSnapshot(), r => r.Level >= LogLevel.Error);
        }

        // And with a handler that logs the exception itself, the error is there with its exception
        var (logging, logs) = await StartAsync<UnexpectedExceptionHandler>();
        await using (logging)
        {
            using var response = await logging.GetTestClient().GetAsync("/boom");

            var error = Assert.Single(logs.GetSnapshot(), r => r.Level >= LogLevel.Error);
            Assert.IsType<InvalidOperationException>(error.Exception);
        }
    }

    // A worker written for .NET 8: it does its set-up before the first await, and on 8 the host waited for it.
    private sealed class SetUpBeforeFirstAwait(ManualResetEventSlim gate) : BackgroundService
    {
        public bool SetUpDone { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            gate.Wait(stoppingToken);   // stands in for set-up that takes a while, synchronously
            SetUpDone = true;
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task WorkerSetUpBeforeItsFirstAwait_OnDotNet10_IsNotDoneWhenTheHostHasStarted()
    {
        // Given a worker whose set-up runs before its first await
        using var gate = new ManualResetEventSlim();
        var worker = new SetUpBeforeFirstAwait(gate);
        using var host = Host.CreateApplicationBuilder().Apply(b => b.Services.AddHostedService(_ => worker)).Build();

        // When the host starts, then start-up doesn't wait for that set-up any more
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(worker.SetUpDone);

        gate.Set();
        await host.StopAsync();
    }

    private sealed class SlowWarmUp(TaskCompletionSource started) : IWarmUp
    {
        public bool Done { get; private set; }

        public async Task RunAsync(CancellationToken ct)
        {
            started.SetResult();
            await Task.Yield();
            Done = true;
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task WarmUpInStartAsync_IsDoneBeforeTheHostHasStarted()
    {
        // Given the set-up moved to StartAsync
        var warmUp = new SlowWarmUp(new TaskCompletionSource());
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IWarmUp>(warmUp);
        builder.Services.AddHostedService<WarmUpWorker>();
        using var host = builder.Build();

        // When the host has started, then the set-up is done
        await host.StartAsync();
        Assert.True(warmUp.Done);
        await host.StopAsync();
    }
}

internal static class BuilderExtensions
{
    public static HostApplicationBuilder Apply(this HostApplicationBuilder builder, Action<HostApplicationBuilder> configure)
    {
        configure(builder);
        return builder;
    }
}
