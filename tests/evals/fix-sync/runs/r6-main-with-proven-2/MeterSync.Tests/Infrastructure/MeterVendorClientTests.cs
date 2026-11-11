using MeterSync.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MeterSync.Tests.Infrastructure;

public sealed class MeterVendorClientTests
{
    [Fact]
    public async Task ReadingsAsync_ProductionBaseAddressWithoutTrailingSlash_KeepsTheV2Path()
    {
        await using var factory = new MeterSyncFactory();
        factory.Vendor.Body = """[{ "id": "r-1", "value": "HI", "unit": "mg/dL", "takenAt": "2026-11-02T07:58:00Z" }]""";
        await using var scope = factory.Services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<MeterVendorClient>();

        var readings = await client.ReadingsAsync("ABC-123", CancellationToken.None);

        factory.Vendor.Requests.Should().ContainSingle()
            .Which.RequestUri.Should().Be(new Uri("https://meters.example.net/v2/meters/ABC-123/readings"));
        readings.Should().ContainSingle().Which.Value.Should().Be("HI");
    }

    [Theory]
    [InlineData("https://meters.example.net/v2", "https://meters.example.net/v2/")]
    [InlineData("https://meters.example.net/v2/", "https://meters.example.net/v2/")]
    [InlineData("https://sms.example.net", "https://sms.example.net/")]
    public void WithTrailingSlash_AnyBase_EndsWithOneSlash(string input, string expected)
    {
        BaseAddress.WithTrailingSlash(new Uri(input)).Should().Be(new Uri(expected));
    }
}
