using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MeterSync.Tests.Api;

public sealed class ProgramTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task HealthLive_Anonymous_ReturnsOkWithSecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle()
            .Which.Should().Be("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task GetMeters_NoToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMeters_TokenForAnotherAudience_ReturnsUnauthorized()
    {
        var response = await Send(HttpMethod.Get, ApiFactory.TokenFor("1", audience: "another-api"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    public async Task GetMeters_TokenWithoutIntegerSubject_ReturnsForbidden(string? sub)
    {
        var response = await Send(HttpMethod.Get, ApiFactory.TokenFor(sub));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("12345678901", "ABC-1")]
    [InlineData("", "ABC-1")]
    [InlineData("1234567890", "")]
    public async Task PostMeters_InvalidBody_ReturnsBadRequest(string nationalId, string serialNumber)
    {
        var response = await Send(HttpMethod.Post, ApiFactory.TokenFor("1"), new { nationalId, serialNumber });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preflight_ClinicOrigin_IsAllowedWithoutCredentials()
    {
        var response = await Preflight("https://clinic.example.net");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://clinic.example.net");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_UnknownOrigin_IsNotAllowed()
    {
        var response = await Preflight("https://evil.example.com");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public void HttpClients_Configured_VendorKeepsVersionAndSmsHasShortTimeout()
    {
        var clients = factory.Services.GetRequiredService<IHttpClientFactory>();

        clients.CreateClient(nameof(MeterVendorClient)).BaseAddress!.AbsoluteUri.Should().Be("https://meters.example.net/v2/");
        clients.CreateClient(nameof(SmsClient)).Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Services_FromScope_ResolveSyncAndSweeper()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<MeterSyncService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<AlertSweeper>().Should().NotBeNull();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, "/meters");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private async Task<HttpResponseMessage> Preflight(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return await factory.CreateClient().SendAsync(request);
    }
}
