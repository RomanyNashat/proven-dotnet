using System.Net;
using FluentAssertions;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeterSync.Tests;

// Nothing here reaches the database: the background worker is removed and no endpoint that queries runs.
public sealed class ProgramTests : IClassFixture<ProgramTests.App>
{
    private readonly App _app;

    public ProgramTests(App app) => _app = app;

    [Fact]
    public async Task HealthLive_Anonymous_ReturnsOk()
    {
        var response = await _app.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMeters_NoToken_ReturnsUnauthorized()
    {
        var response = await _app.CreateClient().GetAsync(new Uri("/meters", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("https://clinic.example.net", true)]
    [InlineData("https://evil.example.com", false)]
    public async Task Preflight_Origin_AllowedOnlyForClinic(string origin, bool allowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _app.CreateClient().SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().Be(allowed);
    }

    [Fact]
    public void VendorClient_RelativePath_StaysUnderV2()
    {
        var factory = _app.Services.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(nameof(MeterVendorClient));

        new Uri(http.BaseAddress!, "meters/abc/readings").AbsoluteUri
            .Should().Be("https://meters.example.net/v2/meters/abc/readings");
    }

    public sealed class App : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(s =>
            {
                var worker = s.Single(d => d.ImplementationType?.Name == "SyncWorker");
                s.Remove(worker);
            });
        }
    }
}
