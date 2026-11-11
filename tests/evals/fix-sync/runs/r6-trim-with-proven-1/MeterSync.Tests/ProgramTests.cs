using System.Net;
using System.Net.Http.Headers;
using MeterSync.Application;
using MeterSync.Infrastructure;
using MeterSync.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace MeterSync.Tests;

public sealed class ProgramTests(MeterSyncFactory factory) : IClassFixture<MeterSyncFactory>
{
    [Fact]
    public void Services_ScopedServiceFromRoot_ThrowsBecauseScopesAreValidated()
    {
        var resolve = () => factory.Services.GetRequiredService<PatientDirectory>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*scoped*");
    }

    [Fact]
    public void Services_SyncAndSweeperFromScope_Resolve()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<MeterSyncService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<AlertSweeper>().Should().NotBeNull();
    }

    [Fact]
    public void VendorClient_ConfiguredBaseWithoutSlash_KeepsV2Segment()
    {
        var http = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MeterVendorClient));

        new Uri(http.BaseAddress!, "meters/A1/readings").AbsoluteUri
            .Should().Be("https://meters.example.net/v2/meters/A1/readings");
    }

    [Fact]
    public void SmsClient_Timeout_IsFifteenSeconds()
    {
        var http = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SmsClient));

        http.Timeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Authentication_TokenForAnotherAudience_Returns401()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/meters");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MeterSyncFactory.Token(audience: "another-api"));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_Anonymous_Returns200WithSecurityHeaders()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'");
    }

    [Fact]
    public async Task Meters_Anonymous_Returns401()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cors_PreflightFromClinic_AllowsOriginWithoutCredentials()
    {
        using var response = await PreflightAsync("https://clinic.example.net");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://clinic.example.net");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task Cors_PreflightFromOtherOrigin_IsNotAllowed()
    {
        using var response = await PreflightAsync("https://evil.example.com");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task UnhandledError_DatabaseUnreachable_Returns500ProblemDetailsWithoutInternals()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/meters");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MeterSyncFactory.Token());

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        body.Should().NotContainAny("Npgsql", "   at ", "localhost");
    }

    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return await client.SendAsync(request);
    }
}
