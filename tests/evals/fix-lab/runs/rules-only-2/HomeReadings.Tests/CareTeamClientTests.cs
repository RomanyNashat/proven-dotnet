using System.Net;
using FluentAssertions;
using HomeReadings.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HomeReadings.Tests;

public sealed class CareTeamClientTests
{
    private static readonly DateTime CollectedAt = new(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SendAlertAsync_Accepted_IsDeliveredAfterOnePost()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var result = await Send(handler);

        result.Should().Be(AlertDelivery.Delivered);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAlertAsync_ServiceUnavailable_IsNotDeliveredAndNotRetried()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await Send(handler);

        result.Should().Be(AlertDelivery.NotDelivered);
        handler.Calls.Should().Be(1, "every POST is a new alert to a nurse");
    }

    [Fact]
    public async Task SendAlertAsync_ConnectionRefused_IsNotDelivered()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        (await Send(handler)).Should().Be(AlertDelivery.NotDelivered);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAlertAsync_ConnectionDroppedAfterSending_IsUnknownAndNotRetried()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException(HttpRequestError.ResponseEnded, "dropped"));

        (await Send(handler)).Should().Be(AlertDelivery.Unknown);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAlertAsync_Timeout_IsUnknown()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));

        (await Send(handler)).Should().Be(AlertDelivery.Unknown);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAlertAsync_PostsValueInMmolPerL()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Send(handler, 11.1m);

        body.Should().Contain("\"nationalId\":\"1000000001\"").And.Contain("\"value\":11.1").And.Contain("2026-10-22T05:00:00Z");
    }

    private static async Task<AlertDelivery> Send(StubHandler handler, decimal value = 12.5m)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<CareTeamOptions>().Configure(o => o.BaseAddress = new Uri("https://careteam.test/api/"));
        services.AddCareTeamClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        return await provider.GetRequiredService<CareTeamClient>().SendAlertAsync("1000000001", value, CollectedAt, CancellationToken.None);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri("https://careteam.test/api/alerts"));
            return Task.FromResult(respond(request));
        }
    }
}
