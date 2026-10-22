using System.Net;

namespace HomeReadings.Tests.Api;

public sealed class HealthTests(HomeReadingsFactory factory) : IClassFixture<HomeReadingsFactory>
{
    [Fact]
    public async Task Live_DatabaseDown_Returns200()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_DatabaseDown_Returns503()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
