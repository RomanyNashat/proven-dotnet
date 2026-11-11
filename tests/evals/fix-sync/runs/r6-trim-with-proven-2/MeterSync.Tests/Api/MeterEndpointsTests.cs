using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MeterSync.Tests.Api;

// Only the paths that answer before the database is touched; the patient lookup needs PostgreSQL.
public sealed class MeterEndpointsTests(MeterSyncFactory factory) : IClassFixture<MeterSyncFactory>
{
    [Theory]
    [InlineData("123456789", "SN-1")]
    [InlineData("12345678901", "SN-1")]
    [InlineData("12345abcde", "SN-1")]
    [InlineData("", "SN-1")]
    [InlineData("1234567890", "")]
    [InlineData("1234567890", "-SN")]
    [InlineData("1234567890", "SN 1")]
    [InlineData("1234567890", "SN-1\n")]
    [InlineData("1234567890", "A12345678901234567890123456789012345678901")]
    public async Task Link_InvalidBody_ReturnsValidationProblem(string nationalId, string serialNumber)
    {
        using var response = await PostAsync(MeterSyncFactory.TokenFor("1"), new { nationalId, serialNumber });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Link_SubIsNotAnInteger_ReturnsUnauthorized()
    {
        using var response = await PostAsync(MeterSyncFactory.TokenFor("abc"), new { nationalId = "1", serialNumber = "" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_SubIsNotAnInteger_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MeterSyncFactory.TokenFor("abc"));

        using var response = await client.GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Link_TokenForAnotherAudience_ReturnsUnauthorized()
    {
        using var response = await PostAsync(MeterSyncFactory.TokenFor("1", audience: "other-api"), new { nationalId = "1", serialNumber = "" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, object body)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.PostAsJsonAsync("/meters", body);
    }
}
