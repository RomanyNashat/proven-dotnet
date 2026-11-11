using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeterSync.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MeterSync.Tests;

public sealed class ProgramTests(MeterSyncFactory factory) : IClassFixture<MeterSyncFactory>
{
    private HttpClient Authorized(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task HealthLive_Anonymous_Returns200()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMeters_NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMeters_TokenForAnotherAudience_Returns401()
    {
        var response = await Authorized(MeterSyncFactory.Token(audience: "another-api")).GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    public async Task GetMeters_TokenWithoutPatientId_Returns401(string? sub)
    {
        var response = await Authorized(MeterSyncFactory.Token(sub: sub)).GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    public async Task PostMeters_TokenWithoutPatientId_Returns401(string? sub)
    {
        var response = await Authorized(MeterSyncFactory.Token(sub: sub))
            .PostAsJsonAsync("/meters", new { nationalId = "1234567890", serialNumber = "ABC-1" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostMeters_NationalIdTooLong_Returns400ValidationProblem()
    {
        var response = await Authorized(MeterSyncFactory.Token())
            .PostAsJsonAsync("/meters", new { nationalId = "12345678901", serialNumber = "ABC-1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("NationalId");
    }

    [Fact]
    public void Services_ScopedFromTheRoot_FailsBecauseScopesAreValidated()
    {
        var resolve = () => factory.Services.GetRequiredService<MeterSync.Infrastructure.PatientDirectory>();

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    public async Task PostMeters_BadSerialNumber_Returns400ValidationProblem(string serial)
    {
        var response = await Authorized(MeterSyncFactory.Token())
            .PostAsJsonAsync("/meters", new { nationalId = "1234567890", serialNumber = serial });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("SerialNumber");
    }

    [Fact]
    public async Task Preflight_ClinicOrigin_IsAllowed()
    {
        var response = await factory.CreateClient().SendAsync(Preflight("https://clinic.example.net"));

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://clinic.example.net");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_UnknownOrigin_IsNotAllowed()
    {
        var response = await factory.CreateClient().SendAsync(Preflight("https://evil.example"));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/meters")]
    public async Task AnyResponse_IncludesSecurityHeaders(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task Services_FromAScope_ResolveSyncServiceAndSweeper()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<MeterSyncService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<AlertSweeper>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<TimeProvider>().Should().NotBeNull();
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }
}
