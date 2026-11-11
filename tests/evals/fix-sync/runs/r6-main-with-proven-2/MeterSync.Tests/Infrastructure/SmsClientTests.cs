using System.Net;
using MeterSync.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MeterSync.Tests.Infrastructure;

public sealed class SmsClientTests
{
    [Fact]
    public async Task SendAsync_GatewayReturns500_PostsOnceAndThrows()
    {
        await using var factory = new MeterSyncFactory();
        factory.Sms.Status = HttpStatusCode.InternalServerError;
        await using var scope = factory.Services.CreateAsyncScope();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var send = () => sms.SendAsync("+966500000000", "نص", CancellationToken.None);

        await send.Should().ThrowAsync<HttpRequestException>();
        factory.Sms.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_Success_PostsToMessagesOnTheConfiguredGateway()
    {
        await using var factory = new MeterSyncFactory();
        factory.Sms.Body = "{}";
        await using var scope = factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<SmsClient>().SendAsync("+966500000000", "نص", CancellationToken.None);

        var request = factory.Sms.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://sms.example.net/messages"));
    }

    [Fact]
    public void Registration_SmsHttpClient_HasFifteenSecondTimeout()
    {
        using var factory = new MeterSyncFactory();

        var http = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SmsClient));

        http.Timeout.Should().Be(TimeSpan.FromSeconds(15));
    }
}
