using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace ClinicQueue.Tests;

public sealed class EndpointTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task WhoAmI_ForwardedByNginx_ReturnsOriginalClientIp()
    {
        var client = AuthenticatedClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");

        var caller = await client.GetFromJsonAsync<CallerBody>("/whoami");

        caller!.ClientIp.Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task GetClinics_FirstRequestAfterStart_ReturnsEveryClinic()
    {
        var clinics = await AuthenticatedClient().GetFromJsonAsync<ClinicBody[]>("/clinics");

        clinics.Should().BeEquivalentTo([new ClinicBody(1, "North"), new ClinicBody(2, "South")]);
    }

    [Fact]
    public async Task GetClinics_NoToken_Returns401()
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

    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFactory.TokenFor(42));
        return client;
    }

    private sealed record CallerBody(string ClientIp);

    private sealed record ClinicBody(int Id, string Name);
}
