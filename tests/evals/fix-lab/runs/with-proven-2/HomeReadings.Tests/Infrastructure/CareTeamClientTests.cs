using System.Net;
using HomeReadings.Infrastructure;
using HomeReadings.Tests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace HomeReadings.Tests.Infrastructure;

public sealed class CareTeamClientTests(HomeReadingsFactory factory) : IClassFixture<HomeReadingsFactory>
{
    [Fact]
    public async Task SendAlertAsync_CareTeamReturns503_PostsOnceAndThrows()
    {
        factory.CareTeam.Status = HttpStatusCode.ServiceUnavailable;
        using var scope = factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<CareTeamClient>();

        var send = () => client.SendAlertAsync("1012345678", 12.5m, DateTime.UtcNow, CancellationToken.None);

        await send.Should().ThrowAsync<HttpRequestException>();
        factory.CareTeam.Calls.Should().Be(1, "the care team has no idempotency key, so a retried POST is a second nurse alert");
    }
}
