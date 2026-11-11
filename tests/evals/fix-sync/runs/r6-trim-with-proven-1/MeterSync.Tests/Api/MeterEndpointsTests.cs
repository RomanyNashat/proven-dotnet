using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeterSync.Tests.Support;

namespace MeterSync.Tests.Api;

/// <summary>
/// Only the answers given before the database is touched run here (no PostgreSQL). Ownership (NotFound for
/// another patient's national id) and the unique-violation Conflict need PostgreSQL.
/// </summary>
public sealed class MeterEndpointsTests(MeterSyncFactory factory) : IClassFixture<MeterSyncFactory>
{
    [Fact]
    public async Task Link_TokenWithoutSub_Returns403()
    {
        using var response = await PostAsync(MeterSyncFactory.Token(sub: null), new { nationalId = "1012345678", serialNumber = "A1" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("0")]
    public async Task List_TokenWithUnusableSub_Returns403(string sub)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/meters");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MeterSyncFactory.Token(sub));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("../admin")]
    [InlineData("A1/../../x")]
    [InlineData("")]
    [InlineData("-leading-dash")]
    [InlineData("A1234567890123456789012345678901234567890")]
    public async Task Link_InvalidSerialNumber_Returns400(string serialNumber)
    {
        using var response = await PostAsync(MeterSyncFactory.Token(), new { nationalId = "1012345678", serialNumber });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SerialNumber");
    }

    [Theory]
    [InlineData("")]
    [InlineData("10123456789")]
    [InlineData("10a2345678")]
    [InlineData("+101234567")]
    public async Task Link_InvalidNationalId_Returns400(string nationalId)
    {
        using var response = await PostAsync(MeterSyncFactory.Token(), new { nationalId, serialNumber = "A1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NationalId");
    }

    private async Task<HttpResponseMessage> PostAsync(string token, object body)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/meters") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
