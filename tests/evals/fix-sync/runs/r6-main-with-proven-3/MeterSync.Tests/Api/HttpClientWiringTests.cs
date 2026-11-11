using System.Net;
using MeterSync.Application;
using MeterSync.Infrastructure;
using MeterSync.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MeterSync.Tests.Api;

/// <summary>How Program wires the typed clients, checked through the real host with a stub at the network edge.</summary>
public sealed class HttpClientWiringTests
{
    [Fact]
    public async Task SmsClient_GatewayFails_IsSentExactlyOnce()
    {
        var gateway = new StubHandler(HttpStatusCode.ServiceUnavailable);
        await using var factory = Factory(sms: gateway);
        using var scope = factory.Services.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<SmsClient>().SendAsync("+966500000000", "x", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        gateway.Requests.Should().ContainSingle("the gateway has no idempotency key, so a retry could text the patient twice");
    }

    [Fact]
    public async Task SmsClient_Configured_HasShortTimeout()
    {
        await using var factory = Factory();

        var http = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SmsClient));

        http.Timeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task MeterVendorClient_ConfiguredBaseWithoutSlash_CallsUnderV2()
    {
        var vendor = new StubHandler(HttpStatusCode.OK, "[]");
        await using var factory = Factory(vendor: vendor);
        using var scope = factory.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<MeterVendorClient>().ReadingsAsync("ABC-1", CancellationToken.None);

        vendor.Requests.Should().ContainSingle()
            .Which.Request.RequestUri.Should().Be(new Uri("https://meters.example.net/v2/meters/ABC-1/readings"));
    }

    private static WebApplicationFactory<Program> Factory(StubHandler? sms = null, StubHandler? vendor = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(d => d.ImplementationType == typeof(SyncWorker)));
                if (sms is not null)
                {
                    services.AddHttpClient<SmsClient>().ConfigurePrimaryHttpMessageHandler(() => sms);
                }

                if (vendor is not null)
                {
                    services.AddHttpClient<MeterVendorClient>().ConfigurePrimaryHttpMessageHandler(() => vendor);
                }
            });
        });
}
