using System.Net;
using MeterSync.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeterSync.Tests.Api;

/// <summary>Host-level checks that need no database: DI validation, headers, auth and CORS.</summary>
public sealed class ProgramTests : IClassFixture<ProgramTests.Factory>
{
    private readonly Factory _factory;

    public ProgramTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task HealthLive_Anonymous_ReturnsOkWithSecurityHeaders()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task GetMeters_NoToken_ReturnsUnauthorizedWithSecurityHeaders()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
    }

    [Fact]
    public async Task Preflight_ClinicOrigin_IsAllowedWithoutCredentials()
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Preflight("https://clinic.example.net"));

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://clinic.example.net");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_OtherOrigin_IsNotAllowed()
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Preflight("https://evil.example.com"));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public void Services_ScopedServices_ResolveFromAScope()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<MeterSyncService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<AlertSweeper>().Should().NotBeNull();
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                // No database here: keep the background worker from running.
                var worker = services.Single(d => d.ImplementationType == typeof(SyncWorker));
                services.Remove(worker);
            });
        }
    }
}
