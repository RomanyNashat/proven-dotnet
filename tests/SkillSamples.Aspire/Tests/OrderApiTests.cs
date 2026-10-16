using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace AppHost.Tests;

public sealed class OrderApiTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;
    private HttpClient _http = null!;

    private sealed record Order(long Id, int ProductId);

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        // WaitFor in the AppHost holds the service until Postgres and Redis are up; wait for the service here.
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("order-service").WaitAsync(TimeSpan.FromMinutes(5));
        _http = _app.CreateHttpClient("order-service");
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task CreateOrder_ThenReadIt_ThroughTheRedisTheAppHostWired()
    {
        using var created = await _http.PostAsJsonAsync("/api/orders", new { ProductId = 7, Quantity = 2 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var order = await _http.GetFromJsonAsync<Order>(created.Headers.Location);

        Assert.Equal(7, order!.ProductId);
    }
}
