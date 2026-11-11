using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MeterSync.Tests;

// Starts the real host in Production with no database reachable: the container is validated
// (ValidateScopes/ValidateOnBuild) and the pipeline runs; the worker's failed sync is only logged.
public sealed class ProgramTests : IClassFixture<ProgramTests.ProductionFactory>
{
    private readonly HttpClient _client;

    public ProgramTests(ProductionFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task HealthLive_Anonymous_Returns200WithSecurityHeaders()
    {
        using var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Meters_NoToken_Returns401(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/meters");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Preflight_UnknownOrigin_GetsNoCorsGrant()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", "https://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_ClinicOrigin_IsAllowedWithoutCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", "https://clinic.example.net");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await _client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://clinic.example.net");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    public sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Production")
                .UseSetting("ConnectionStrings:Sync", "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1");
    }
}
