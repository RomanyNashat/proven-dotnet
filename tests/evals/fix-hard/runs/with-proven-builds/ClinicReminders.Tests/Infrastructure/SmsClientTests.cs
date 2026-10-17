using System.Net;
using ClinicReminders.Infrastructure;
using ClinicReminders.Tests.Support;

namespace ClinicReminders.Tests.Infrastructure;

public sealed class SmsClientTests
{
    private static readonly Uri BaseAddress = new("https://sms.example.net/api/v2/");

    [Fact]
    public async Task SendAsync_ProviderAccepts_ReturnsSentAndKeepsTheBasePath()
    {
        var handler = StubSmsHandler.Answering(HttpStatusCode.OK);

        var result = await Client(handler).SendAsync("0501234567", "نص", CancellationToken.None);

        result.Delivery.Should().Be(SmsDelivery.Sent);
        handler.Requests.Should().ContainSingle().Which.Uri.Should().Be(new Uri("https://sms.example.net/api/v2/messages"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendAsync_ProviderAnswersWithAnError_ReturnsNotSentWithTheStatus(HttpStatusCode status)
    {
        var result = await Client(StubSmsHandler.Answering(status)).SendAsync("0501234567", "نص", CancellationToken.None);

        result.Should().Be(new SmsResult(SmsDelivery.NotSent, (int)status, null));
    }

    [Fact]
    public async Task SendAsync_ConnectionRefused_ReturnsNotSent()
    {
        var refused = new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        var result = await Client(StubSmsHandler.Throwing(refused)).SendAsync("0501234567", "نص", CancellationToken.None);

        result.Delivery.Should().Be(SmsDelivery.NotSent);
    }

    [Fact]
    public async Task SendAsync_Timeout_ReturnsUnknown()
    {
        var timedOut = new TaskCanceledException("timeout", new TimeoutException());

        var result = await Client(StubSmsHandler.Throwing(timedOut)).SendAsync("0501234567", "نص", CancellationToken.None);

        result.Should().Be(new SmsResult(SmsDelivery.Unknown, null, nameof(TimeoutException)));
    }

    [Fact]
    public async Task SendAsync_ResponseCutOff_ReturnsUnknown()
    {
        var cut = new HttpRequestException(HttpRequestError.ResponseEnded, "ended");

        var result = await Client(StubSmsHandler.Throwing(cut)).SendAsync("0501234567", "نص", CancellationToken.None);

        result.Delivery.Should().Be(SmsDelivery.Unknown);
    }

    [Fact]
    public async Task SendAsync_CallerCancels_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubSmsHandler((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));

        var act = () => Client(handler).SendAsync("0501234567", "نص", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static SmsClient Client(HttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = BaseAddress });
}
