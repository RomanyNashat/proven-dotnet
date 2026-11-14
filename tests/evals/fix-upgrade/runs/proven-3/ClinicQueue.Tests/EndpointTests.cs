using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClinicQueue.Application;

namespace ClinicQueue.Tests;

public sealed class EndpointTests(ClinicQueueFactory factory) : IClassFixture<ClinicQueueFactory>
{
    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("203.0.113.9, 10.42.0.3", "203.0.113.9")]
    [InlineData("198.51.100.1, 203.0.113.9, 10.42.0.3", "203.0.113.9")]
    public async Task WhoAmI_FromNginxWithForwardedFor_ReturnsClientIp(string forwardedFor, string expected)
    {
        using var request = Authorized(HttpMethod.Get, "/whoami");
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        using var response = await factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        body.Should().ContainKey("clientIp").WhoseValue.Should().Be(expected);
    }

    [Fact]
    public async Task Clinics_AfterStart_ReturnsEveryClinicFromTheSource()
    {
        using var request = Authorized(HttpMethod.Get, "/clinics");

        using var response = await factory.CreateClient().SendAsync(request);

        (await response.Content.ReadFromJsonAsync<List<ClinicEntry>>()).Should().Equal(ClinicQueueFactory.Clinics);
    }

    [Fact]
    public async Task Clinics_NoToken_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(new Uri("/clinics", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_NoToken_Returns200()
    {
        using var response = await factory.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Queue_DayNotADate_Returns400()
    {
        using var request = Authorized(HttpMethod.Get, "/clinics/1/queue?day=14-11-2026");

        using var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VisitDay_SubNotAPatientId_Returns403()
    {
        using var request = Authorized(HttpMethod.Get, "/visits/1/day", "f81d4fae-7dec-11d0-a765-00a0c91e6bf6");

        using var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BookVisit_SubNotAPatientId_Returns403()
    {
        using var request = Authorized(HttpMethod.Post, "/visits", "f81d4fae-7dec-11d0-a765-00a0c91e6bf6");
        request.Content = JsonContent.Create(new { clinicId = 1, day = "2026-11-14" });

        using var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Responses_Always_SayNoSniff()
    {
        using var response = await factory.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative));

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string subject = "42")
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ClinicQueueFactory.TokenFor(subject));
        return request;
    }
}
