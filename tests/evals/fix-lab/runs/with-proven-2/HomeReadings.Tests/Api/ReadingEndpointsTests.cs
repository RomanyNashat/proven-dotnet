using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace HomeReadings.Tests.Api;

public sealed class ReadingEndpointsTests(HomeReadingsFactory factory) : IClassFixture<HomeReadingsFactory>
{
    private static object Body(string value = "11.5", string unit = "mmol/L", string nationalId = "1012345678", string? notes = null) =>
        new { nationalId, value, unit, collectedAt = DateTime.UtcNow.AddMinutes(-5), notes };

    [Theory]
    [InlineData("HI")]
    [InlineData("LO")]
    [InlineData("eleven")]
    [InlineData("")]
    public async Task PostReading_ValueIsNotANumber_Returns400(string value)
    {
        using var client = factory.CreatePatientClient();

        var response = await client.PostAsJsonAsync("/readings", Body(value: value));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostReading_MeterShowsHiInArabic_TellsThePatientToContactTheCareTeamInArabic()
    {
        using var client = factory.CreatePatientClient(language: "ar");

        var response = await client.PostAsJsonAsync("/readings", Body(value: "HI"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("فريق الرعاية");
    }

    [Theory]
    [InlineData("10123456789")]
    [InlineData("12345")]
    [InlineData("10123x5678")]
    public async Task PostReading_NationalIdNotTenDigits_Returns400(string nationalId)
    {
        using var client = factory.CreatePatientClient();

        var response = await client.PostAsJsonAsync("/readings", Body(nationalId: nationalId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("stones")]
    [InlineData("mmol/L but longer than sixteen")]
    public async Task PostReading_UnknownUnit_Returns400(string unit)
    {
        using var client = factory.CreatePatientClient();

        var response = await client.PostAsJsonAsync("/readings", Body(unit: unit));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostReading_NotesLongerThanColumn_Returns400()
    {
        using var client = factory.CreatePatientClient();

        var response = await client.PostAsJsonAsync("/readings", Body(notes: new string('x', 501)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostReading_CollectedInTheFuture_Returns400()
    {
        using var client = factory.CreatePatientClient();
        var body = new { nationalId = "1012345678", value = "6.1", unit = "mmol/L", collectedAt = DateTime.UtcNow.AddHours(2), notes = (string?)null };

        var response = await client.PostAsJsonAsync("/readings", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetReading_DatabaseFails_Returns500WithoutInternals()
    {
        using var client = factory.CreatePatientClient();

        var response = await client.GetAsync("/readings/1");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        body.Should().Contain("traceId").And.NotContain("127.0.0.1").And.NotContain("connect").And.NotContain("Npgsql");
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("3f2b5c1e-8a1d-4f7e-9a51-6c0f9a3e2b11")]
    public async Task GetReading_SubjectIsNotAPatientId_Returns403(string subject)
    {
        using var client = factory.CreatePatientClient(subject);

        var response = await client.GetAsync("/readings/1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetReading_NoToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/readings/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("text/plain", "notes.txt")]
    [InlineData("application/x-sh", "../../../evil.sh")]
    public async Task PostPhoto_NotAnImage_Returns400(string contentType, string fileName)
    {
        using var client = factory.CreatePatientClient();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "photo", fileName);

        var response = await client.PostAsync("/readings/1/photo", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnyResponse_CarriesSecurityHeaders()
    {
        using var client = factory.CreatePatientClient();

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
    }
}
