using System.Net;
using MeterSync.Infrastructure;

namespace MeterSync.Tests.Infrastructure;

public sealed class MeterVendorClientTests
{
    private const string Body = """[{ "id": "r-8812", "value": "112", "unit": "mg/dL", "takenAt": "2026-11-02T07:58:00Z" }]""";

    private static (MeterVendorClient Client, StubHandler Handler) Create(HttpStatusCode status = HttpStatusCode.OK, string body = Body)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = BaseAddress.WithTrailingSlash("https://meters.example.net/v2") };
        return (new MeterVendorClient(http), handler);
    }

    [Fact]
    public async Task ReadingsAsync_ConfiguredBaseWithoutSlash_KeepsV2InPath()
    {
        var (client, handler) = Create();

        await client.ReadingsAsync("ABC-123", CancellationToken.None);

        handler.Requests.Should().ContainSingle()
            .Which.Request.RequestUri.Should().Be(new Uri("https://meters.example.net/v2/meters/ABC-123/readings"));
    }

    [Fact]
    public async Task ReadingsAsync_VendorJson_ReturnsReadings()
    {
        var (client, _) = Create();

        var readings = await client.ReadingsAsync("ABC-123", CancellationToken.None);

        readings.Should().ContainSingle().Which.Should().Be(
            new VendorReading("r-8812", "112", "mg/dL", new DateTimeOffset(2026, 11, 2, 7, 58, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task ReadingsAsync_VendorError_Throws()
    {
        var (client, _) = Create(HttpStatusCode.BadGateway, "");

        var act = () => client.ReadingsAsync("ABC-123", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
