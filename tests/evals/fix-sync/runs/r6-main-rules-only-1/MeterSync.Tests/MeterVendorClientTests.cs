using System.Net;
using System.Text;
using FluentAssertions;
using MeterSync.Infrastructure;
using Xunit;

namespace MeterSync.Tests;

public sealed class MeterVendorClientTests
{
    [Fact]
    public async Task ReadingsAsync_BaseAddressWithoutTrailingSlash_KeepsVersionSegment()
    {
        var handler = new RecordingHandler("""[{ "id": "r-8812", "value": "112", "unit": "mg/dL", "takenAt": "2026-11-02T07:58:00Z" }]""");
        using var http = new HttpClient(handler) { BaseAddress = HttpBaseAddress.From("https://meters.example.net/v2") };

        var readings = await new MeterVendorClient(http).ReadingsAsync("AB 12", CancellationToken.None);

        handler.LastUri.Should().Be(new Uri("https://meters.example.net/v2/meters/AB%2012/readings"));
        readings.Should().ContainSingle().Which.Id.Should().Be("r-8812");
    }

    [Fact]
    public void From_AddressWithTrailingSlash_IsUnchanged() =>
        HttpBaseAddress.From("https://sms.example.net/").Should().Be(new Uri("https://sms.example.net/"));

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
