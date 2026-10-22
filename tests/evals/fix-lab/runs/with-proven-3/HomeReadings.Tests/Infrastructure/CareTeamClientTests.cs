using System.Net;
using HomeReadings.Infrastructure;
using HomeReadings.Tests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HomeReadings.Tests.Infrastructure;

/// <summary>The client as Program registers it, with the care team's system replaced by a stub.</summary>
public sealed class CareTeamClientTests
{
    private static readonly CareTeamAlertMessage Alert =
        new("1012345678", 15.2m, "mmol/L", null, new DateTime(2026, 10, 22, 5, 30, 0, DateTimeKind.Utc));

    [Fact]
    public async Task SendAlert_CareTeamAnswers503_PostsOnlyOnce()
    {
        var careTeam = RecordingHandler.Answering(HttpStatusCode.ServiceUnavailable);

        var delivery = await SendThrough(careTeam);

        careTeam.Calls.Should().Be(1, "the care team has no idempotency key: a second POST pages a nurse twice");
        delivery.Outcome.Should().Be(AlertDeliveryOutcome.Failed);
    }

    [Fact]
    public async Task SendAlert_CareTeamAccepts_IsDeliveredWithTheUnit()
    {
        var careTeam = RecordingHandler.Answering(HttpStatusCode.Accepted);

        var delivery = await SendThrough(careTeam);

        delivery.Outcome.Should().Be(AlertDeliveryOutcome.Delivered);
        careTeam.Bodies.Should().ContainSingle().Which.Should()
            .Contain("\"nationalId\":\"1012345678\"").And.Contain("\"value\":15.2").And.Contain("\"unit\":\"mmol/L\"")
            .And.Contain("\"collectedAt\":\"2026-10-22T05:30:00Z\"");
    }

    [Fact]
    public async Task SendAlert_CareTeamRefusesTheRequest_IsRejected()
    {
        var delivery = await SendThrough(RecordingHandler.Answering(HttpStatusCode.BadRequest));

        delivery.Outcome.Should().Be(AlertDeliveryOutcome.Rejected);
        delivery.Reason.Should().Be("HTTP 400");
    }

    [Fact]
    public async Task SendAlert_CannotConnect_FailedAndSafeToRetry()
    {
        var delivery = await SendThrough(new RecordingHandler(_ =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")));

        delivery.Outcome.Should().Be(AlertDeliveryOutcome.Failed);
    }

    [Fact]
    public async Task SendAlert_ConnectionDropsAfterSending_IsUnknown()
    {
        var delivery = await SendThrough(new RecordingHandler(_ =>
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "ended")));

        delivery.Outcome.Should().Be(AlertDeliveryOutcome.Unknown);
    }

    [Fact]
    public async Task SendAlert_NoAnswerInTime_IsUnknown()
    {
        var delivery = await SendThrough(new RecordingHandler(_ => throw new TaskCanceledException("timed out")));

        delivery.Outcome.Should().Be(AlertDeliveryOutcome.Unknown);
    }

    private static async Task<AlertDelivery> SendThrough(RecordingHandler careTeam)
    {
        using var factory = new ServiceFactory().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHttpClient<CareTeamClient>().ConfigurePrimaryHttpMessageHandler(() => careTeam)));
        var client = factory.Services.GetRequiredService<CareTeamClient>();
        return await client.SendAlertAsync(Alert, CancellationToken.None);
    }
}
