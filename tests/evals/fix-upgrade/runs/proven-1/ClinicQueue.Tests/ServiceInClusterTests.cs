using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace ClinicQueue.Tests;

public sealed class ServiceInClusterTests : IClassFixture<ServiceFactory>
{
    private readonly ServiceFactory _factory;

    public ServiceInClusterTests(ServiceFactory factory) => _factory = factory;

    [Fact]
    public async Task GetClinics_FirstRequestAfterStart_ReturnsEveryClinic()
    {
        using var client = AuthenticatedClient();

        var clinics = await client.GetFromJsonAsync<List<ClinicDto>>("/clinics");

        clinics.Should().BeEquivalentTo([new ClinicDto(1, "North"), new ClinicDto(2, "South")]);
    }

    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("203.0.113.9, 10.42.3.4", "203.0.113.9")]
    [InlineData("198.51.100.1, 203.0.113.9, 10.42.3.4", "203.0.113.9")]
    public async Task WhoAmI_ArrivesThroughNginx_ReturnsClientAddress(string forwardedFor, string expected)
    {
        using var client = AuthenticatedClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);

        var caller = await client.GetFromJsonAsync<CallerDto>("/whoami");

        caller!.ClientIp.Should().Be(expected);
    }

    [Fact]
    public async Task HealthLive_NoToken_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'")]
    public async Task AnyResponse_SecurityHeaders_AreSet(string header, string expected)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues(header).Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public async Task GetClinics_NoToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/clinics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.TokenFor(42));
        return client;
    }

    private sealed record ClinicDto(int Id, string Name);

    private sealed record CallerDto(string ClientIp);
}
