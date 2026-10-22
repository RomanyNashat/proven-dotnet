using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeReadings.Tests.Support;

namespace HomeReadings.Tests.Api;

/// <summary>
/// What the API decides before it reaches the database. The database here refuses connections, so a
/// request that gets past validation fails; these tests only send requests that must stop earlier.
/// </summary>
public sealed class ReadingEndpointsTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private static object Reading(string value = "6.2", string unit = "mmol/L", string nationalId = "1012345678", string? notes = null) =>
        new { nationalId, value, unit, collectedAt = "2026-10-22T05:30:00Z", notes };

    [Fact]
    public async Task HealthLive_DatabaseUnreachable_ReturnsOk()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_DatabaseUnreachable_ReturnsServiceUnavailable()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task AnyRequest_ArabicLanguageOnImageWithoutIcu_IsServedNormally()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-language", "ar");

        var response = await client.GetAsync("/readings/today");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetToday_DatabaseFails_ReturnsProblemDetailsWithoutInternals()
    {
        var response = await factory.CreatePatientClient().GetAsync("/readings/today");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        body.Should().NotContain("127.0.0.1").And.NotContain("Npgsql").And.NotContain("Exception");
    }

    [Fact]
    public async Task GetToday_SubIsNotAPatientNumber_ReturnsForbidden()
    {
        var response = await factory.CreatePatientClient(sub: "3f1c9a2e-77b0-4c1e-9d53-0d8f5d6c2a11").GetAsync("/readings/today");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-4.0")]
    [InlineData("0")]
    public async Task PostReading_ValueIsNotAMeterReading_ReturnsValidationProblem(string value)
    {
        var response = await factory.CreatePatientClient().PostAsJsonAsync("/readings", Reading(value: value));

        await ShouldBeValidationProblemFor(response, "value");
    }

    [Theory]
    [InlineData("kg")]
    [InlineData("")]
    public async Task PostReading_UnitIsNotAGlucoseUnit_ReturnsValidationProblem(string unit)
    {
        var response = await factory.CreatePatientClient().PostAsJsonAsync("/readings", Reading(unit: unit));

        await ShouldBeValidationProblemFor(response, "unit");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("10123456789")]
    [InlineData("10123x5678")]
    public async Task PostReading_NationalIdIsNotTenDigits_ReturnsValidationProblem(string nationalId)
    {
        var response = await factory.CreatePatientClient().PostAsJsonAsync("/readings", Reading(nationalId: nationalId));

        await ShouldBeValidationProblemFor(response, "nationalId");
    }

    [Fact]
    public async Task PostReading_NotesLongerThanTheColumn_ReturnsValidationProblem()
    {
        var response = await factory.CreatePatientClient().PostAsJsonAsync("/readings", Reading(notes: new string('x', 501)));

        await ShouldBeValidationProblemFor(response, "notes");
    }

    [Fact]
    public async Task PostReading_NoCollectedAt_ReturnsValidationProblem()
    {
        var response = await factory.CreatePatientClient().PostAsJsonAsync("/readings",
            new { nationalId = "1012345678", value = "6.2", unit = "mmol/L", notes = (string?)null });

        await ShouldBeValidationProblemFor(response, "collectedAt");
    }

    [Fact]
    public async Task AnyResponse_CarriesNoStoreAndNoSniff()
    {
        var response = await factory.CreateClient().GetAsync("/readings/today");

        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
    }

    private static async Task ShouldBeValidationProblemFor(HttpResponseMessage response, string field)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue($"the errors should name {field}");
    }
}
