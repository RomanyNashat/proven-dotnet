using HomeReadings.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HomeReadings.Tests.Api;

public sealed class CareTeamResilienceTests(NoDatabaseFactory factory) : IClassFixture<NoDatabaseFactory>
{
    [Fact]
    public async Task SendAlertAsync_CareTeamAnswers503_PostsOnce()
    {
        var client = factory.Services.GetRequiredService<CareTeamClient>();

        var send = () => client.SendAlertAsync("1012345678", 15.2m, new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        await send.Should().ThrowAsync<HttpRequestException>();
        factory.CareTeam.Calls.Should().Be(1, "the care team has no idempotency key: a retried POST is a second nurse alert");
    }
}
