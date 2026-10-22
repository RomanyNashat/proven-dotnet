using System.Net;
using System.Net.Http.Json;

namespace HomeReadings.Tests.Api;

public sealed class ReadingApiTests(NoDatabaseFactory factory) : IClassFixture<NoDatabaseFactory>
{
    private static object ValidReading(string value = "12.5", string unit = "mmol/L", string nationalId = "1012345678", string? notes = null) =>
        new { nationalId, value, unit, collectedAt = "2026-10-22T05:00:00Z", notes };

    [Fact]
    public async Task HealthLive_DatabaseDown_Returns200()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_DatabaseDown_ReturnsUnavailable()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task PostReading_DatabaseFails_ReturnsProblemDetailsWithoutExceptionText()
    {
        var response = await factory.ClientFor("7").PostAsJsonAsync("/readings", ValidReading());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("traceId").And.NotContainAny("connect", "127.0.0.1", "Npgsql", "Exception");
    }

    [Theory]
    [InlineData("12,5", "mmol/L", "1012345678", "en")]   // en-US would read this as 125
    [InlineData("12,5", "mmol/L", "1012345678", "ar")]
    [InlineData("abc", "mmol/L", "1012345678", "en")]
    [InlineData("-3", "mmol/L", "1012345678", "en")]
    [InlineData("12.5", "furlongs", "1012345678", "en")]
    [InlineData("12.5", "mmol/L", "10123456789", "en")]   // 11 characters: was a database error
    [InlineData("12.5", "mmol/L", "3012345678", "en")]
    public async Task PostReading_InvalidInput_Returns400(string value, string unit, string nationalId, string language)
    {
        var client = factory.ClientFor("7");
        client.DefaultRequestHeaders.Add("x-language", language);

        var response = await client.PostAsJsonAsync("/readings", ValidReading(value, unit, nationalId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostReading_NotesLongerThanColumn_Returns400()
    {
        var response = await factory.ClientFor("7").PostAsJsonAsync("/readings", ValidReading(notes: new string('x', 501)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetReading_NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/readings/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetReading_SubjectNotAPatientId_Returns403()
    {
        var response = await factory.ClientFor("4f1c0b5e-9d2a-4c1e-8f00-1a2b3c4d5e6f").GetAsync("/readings/1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetReading_Response_IsNotCachedByClients()
    {
        var response = await factory.ClientFor("4f1c").GetAsync("/readings/1");

        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }
}
