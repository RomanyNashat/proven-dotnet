using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClinicQueue.Api;
using ClinicQueue.Application;
using FluentAssertions;

namespace ClinicQueue.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task WhoAmI_ThroughIngressAndNginx_ReturnsOriginalClient()
    {
        using var request = Authorized(HttpMethod.Get, "/whoami");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9, 10.42.3.1");

        var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, response.Headers.WwwAuthenticate.ToString());
        (await response.Content.ReadFromJsonAsync<Caller>()).Should().Be(new Caller("203.0.113.9"));
    }

    [Fact]
    public async Task Clinics_FirstRequestAfterStart_ReturnsEveryClinic()
    {
        using var request = Authorized(HttpMethod.Get, "/clinics");

        var response = await factory.CreateClient().SendAsync(request);

        (await response.Content.ReadFromJsonAsync<List<ClinicEntry>>()).Should().BeEquivalentTo(FixedClinicSource.Clinics);
    }

    [Fact]
    public async Task Clinics_NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/clinics", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_NoToken_Returns200()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.TokenFor(42));
        return request;
    }
}
