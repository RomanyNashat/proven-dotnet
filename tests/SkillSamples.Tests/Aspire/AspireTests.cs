using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Aspire;

public sealed class AspireTests
{
    private sealed class Recorder : DelegatingHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<WebApplication> StartServiceAsync(List<Activity>? spans = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddServiceDefaults();
        if (spans is not null)
        {
            builder.Services.AddOpenTelemetry().WithTracing(t => t.AddInMemoryExporter(spans));
        }

        var app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapGet("/api/ping", () => "pong");
        await app.StartAsync();
        return app;
    }

    // A downstream payments API that is having a bad minute: every POST gets a 503.
    private static async Task<(WebApplication App, Func<int> Posts)> StartFailingPaymentsAsync()
    {
        var posts = 0;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapPost("/payments", () =>
        {
            Interlocked.Increment(ref posts);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });
        await app.StartAsync();
        return (app, () => Volatile.Read(ref posts));
    }

    private static async Task PostOnceAsync(Action<HostApplicationBuilder> defaults, WebApplication payments)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        defaults(builder);
        builder.Services.AddHttpClient("payments", c => c.BaseAddress = new Uri("http://payments"))
            .ConfigurePrimaryHttpMessageHandler(() => payments.GetTestServer().CreateHandler());
        using var host = builder.Build();
        using var response = await host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("payments")
            .PostAsync("/payments", new StringContent("{\"amount\":100}"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoints_AreMappedInProduction()
    {
        await using var app = await StartServiceAsync();

        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Tracing_RecordsRequests_ButNotTheProbes()
    {
        var spans = new List<Activity>();
        await using var app = await StartServiceAsync(spans);

        await app.GetTestClient().GetAsync("/health/live");
        await app.GetTestClient().GetAsync("/api/ping");
        for (var i = 0; i < 20 && spans.Count == 0; i++)
        {
            await Task.Delay(100);   // the server span ends just after the response
        }

        var paths = spans.Select(s => s.GetTagItem("url.path") as string).ToList();
        Assert.Contains("/api/ping", paths);
        Assert.DoesNotContain("/health/live", paths);
    }

    [Theory]
    [InlineData(false, "http://localhost:5080/orders/7")]
    [InlineData(true, "https://localhost:5443/orders/7")]
    public async Task ServiceDiscovery_HttpsPlusHttp_PrefersHttps_FallsBackToHttp(bool httpsEndpoint, string expected)
    {
        var config = new Dictionary<string, string?> { ["services:orders:http:0"] = "http://localhost:5080" };
        if (httpsEndpoint)
        {
            config["services:orders:https:0"] = "https://localhost:5443";
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(config);
        builder.AddServiceDefaults();
        var recorder = new Recorder();
        builder.Services.AddHttpClient("orders", c => c.BaseAddress = new Uri("https+http://orders"))
            .ConfigurePrimaryHttpMessageHandler(() => recorder);
        using var host = builder.Build();

        await host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("orders").GetAsync("/orders/7");

        Assert.Equal(new Uri(expected), Assert.Single(recorder.Requests));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_PaymentsApiReturns503_TheTemplateSendsThePaymentFourTimes_TheseDefaultsOnce()
    {
        // Given a payments API that answers 503 for a minute (it may still have taken the money)
        var (payments, posts) = await StartFailingPaymentsAsync();
        await using var _ = payments;

        // When a service built on the template's defaults posts one payment
        await PostOnceAsync(b => b.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler()), payments);
        var template = posts();

        // And one built on these defaults posts one payment
        await PostOnceAsync(b => b.AddServiceDefaults(), payments);

        // Then the template sent it four times (one try, three retries); these defaults sent it once
        Assert.Equal(4, template);
        Assert.Equal(1, posts() - template);
    }

    [Fact]
    public void ZeroRetries_IsRejected_SoATestCantTurnRetriesOffThatWay()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("orders").AddStandardResilienceHandler(o => o.Retry.MaxRetryAttempts = 0);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IHttpClientFactory>().CreateClient("orders"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheDefaultsStartAndAnswer()
    {
        ProductionConditions.Require();
        await using var app = await StartServiceAsync();

        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().GetAsync("/health/ready")).StatusCode);
    }
}
