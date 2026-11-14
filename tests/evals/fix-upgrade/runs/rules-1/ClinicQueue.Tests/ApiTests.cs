using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task WhoAmI_ForwardedThroughIngressAndNginx_ReturnsOriginalClientIp()
    {
        using var client = factory.CreateClientAs(42);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9, 10.42.0.3");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, response.Headers.WwwAuthenticate.ToString());
        (await response.Content.ReadFromJsonAsync<CallerBody>())!.ClientIp.Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task Clinics_FirstRequestAfterStart_ReturnsEveryClinic()
    {
        using var client = factory.CreateClientAs(42);

        var clinics = await client.GetFromJsonAsync<ClinicBody[]>("/clinics");

        clinics.Should().BeEquivalentTo([new ClinicBody(1, "North"), new ClinicBody(2, "South")]);
    }

    [Fact]
    public async Task Clinics_NoToken_Returns401()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/clinics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_NoToken_Returns200()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("14/11/2026")]
    public async Task Queue_DayNotIsoDate_Returns400(string day)
    {
        using var client = factory.CreateClientAs(42);

        using var response = await client.GetAsync($"/clinics/1/queue?day={Uri.EscapeDataString(day)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Queue_DatabaseUnreachable_Returns500ProblemAndLogsException()
    {
        using var client = factory.CreateClientAs(42);

        using var response = await client.GetAsync("/clinics/1/queue?day=2026-11-14");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        factory.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Exception != null
            && e.Category.StartsWith("Microsoft.AspNetCore.Diagnostics", StringComparison.Ordinal));
    }

    private sealed record CallerBody(string ClientIp);

    private sealed record ClinicBody(int Id, string Name);
}
