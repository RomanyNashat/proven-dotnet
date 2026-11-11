using System.Net;
using System.Text.Json;
using MeterSync.Infrastructure;

namespace MeterSync.Tests.Infrastructure;

public sealed class SmsClientTests
{
    private static (SmsClient Client, StubHandler Handler) Create(HttpStatusCode status)
    {
        var handler = new StubHandler(status);
        var http = new HttpClient(handler) { BaseAddress = BaseAddress.WithTrailingSlash("https://sms.example.net/") };
        return (new SmsClient(http), handler);
    }

    [Fact]
    public async Task SendAsync_Accepted_PostsToAndTextToMessages()
    {
        var (client, handler) = Create(HttpStatusCode.Accepted);

        await client.SendAsync("+966500000000", "نص", CancellationToken.None);

        var (request, body) = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://sms.example.net/messages"));
        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("to").GetString().Should().Be("+966500000000");
        json.RootElement.GetProperty("text").GetString().Should().Be("نص");
    }

    [Fact]
    public async Task SendAsync_GatewayError_Throws()
    {
        var (client, handler) = Create(HttpStatusCode.InternalServerError);

        var act = () => client.SendAsync("+966500000000", "نص", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle("the gateway is not idempotent, so the client never retries");
    }
}
