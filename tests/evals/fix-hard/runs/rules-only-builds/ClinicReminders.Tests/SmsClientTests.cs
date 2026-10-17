using System.Net;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Tests;

public sealed class SmsClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, SmsOutcome.Sent)]
    [InlineData(HttpStatusCode.Accepted, SmsOutcome.Sent)]
    [InlineData(HttpStatusCode.BadRequest, SmsOutcome.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, SmsOutcome.NotSent)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SmsOutcome.NotSent)]
    [InlineData(HttpStatusCode.RequestTimeout, SmsOutcome.Unknown)]
    [InlineData(HttpStatusCode.InternalServerError, SmsOutcome.Unknown)]
    [InlineData(HttpStatusCode.GatewayTimeout, SmsOutcome.Unknown)]
    public async Task SendAsync_ProviderAnswers_ClassifiesTheOutcome(HttpStatusCode status, SmsOutcome expected)
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(status)));

        var outcome = await Client(handler).SendAsync("+966500000001", "x", CancellationToken.None);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task SendAsync_ConnectionRefused_IsNotSentSoItCanBeRetried()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        var outcome = await Client(handler).SendAsync("+966500000001", "x", CancellationToken.None);

        outcome.Should().Be(SmsOutcome.NotSent);
    }

    [Fact]
    public async Task SendAsync_ConnectionDroppedMidResponse_IsUnknown()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException(HttpRequestError.ResponseEnded, "ended"));

        var outcome = await Client(handler).SendAsync("+966500000001", "x", CancellationToken.None);

        outcome.Should().Be(SmsOutcome.Unknown);
    }

    [Fact]
    public async Task SendAsync_ProviderSlowerThanTheTimeout_IsUnknownNotRetried()
    {
        var handler = new StubHandler(async r =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://sms.test/api/v2/"), Timeout = TimeSpan.FromMilliseconds(100) };

        var outcome = await new SmsClient(http).SendAsync("+966500000001", "x", CancellationToken.None);

        outcome.Should().Be(SmsOutcome.Unknown);
    }

    [Fact]
    public async Task SendAsync_ConfiguredBaseUrlWithoutTrailingSlash_PostsUnderTheVersionPath()
    {
        Uri? requested = null;
        var handler = new StubHandler(r =>
        {
            requested = r.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var options = new SmsOptions { BaseUrl = "https://sms.test/api/v2" };
        var http = new HttpClient(handler) { BaseAddress = options.BaseAddress };

        await new SmsClient(http).SendAsync("+966500000001", "x", CancellationToken.None);

        requested.Should().Be(new Uri("https://sms.test/api/v2/messages"));
    }

    private static SmsClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://sms.test/api/v2/") });

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
