using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Kubernetes;

public sealed class KubernetesTests
{
    private static readonly string Redis = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";

    private static async Task<WebApplication> StartAsync(string redisConnectionString)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProbes(redisConnectionString);
        var app = builder.Build();
        app.MapProbes();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> GetAsync(WebApplication app, string path)
    {
        using var response = await app.GetTestClient().GetAsync(path);
        return response.StatusCode;
    }

    [Fact]
    public async Task Probes_EverythingUp_BothAnswer200()
    {
        await using var app = await StartAsync(Redis);

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/health/live"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/health/ready"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_RedisGoesDown_ThePodLeavesTheServiceButIsNotRestarted()
    {
        // Given a pod whose Redis is unreachable
        await using var app = await StartAsync("localhost:1,connectTimeout=1000");

        // When Kubernetes probes it
        var ready = await GetAsync(app, "/health/ready");
        var live = await GetAsync(app, "/health/live");

        // Then readiness fails, so it stops getting traffic, and liveness passes, so it isn't restarted:
        // a restart wouldn't bring Redis back, and with Redis in liveness every pod would restart at once
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready);
        Assert.Equal(HttpStatusCode.OK, live);
    }

    [Fact]
    public void ShutdownTimeout_DefaultsToThirtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new HostOptions().ShutdownTimeout);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(10, true)]
    public async Task Shutdown_InFlightWorkFinishesOnlyWithinShutdownTimeout(int shutdownSeconds, bool drained)
    {
        var worker = new DrainingWorker(TimeSpan.FromSeconds(3));
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(shutdownSeconds));
        builder.Services.AddHostedService(_ => worker);
        using var host = builder.Build();
        await host.StartAsync();
        await worker.Running.WaitAsync(TimeSpan.FromSeconds(10));   // ExecuteAsync starts in the background

        await host.StopAsync();   // SIGTERM

        Assert.Equal(drained, worker.Drained);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheProbesStillAnswer()
    {
        ProductionConditions.Require();
        await using var app = await StartAsync(Redis);

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/health/live"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/health/ready"));
    }
}
