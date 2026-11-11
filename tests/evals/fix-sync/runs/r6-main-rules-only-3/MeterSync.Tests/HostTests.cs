using System.Net;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MeterSync.Tests;

// Runs the real host in Production with stubbed outbound HTTP. Nothing here touches the database.
public sealed class HostTests : IDisposable
{
    private readonly StubHandler _vendor = new(HttpStatusCode.OK, "[]");
    private readonly StubHandler _sms = new(HttpStatusCode.InternalServerError, "");
    private readonly WebApplicationFactory<Program> _factory;

    public HostTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IHostedService>(typeof(SyncWorker));
                s.AddHttpClient<MeterVendorClient>().ConfigurePrimaryHttpMessageHandler(() => _vendor);
                s.AddHttpClient<SmsClient>().ConfigurePrimaryHttpMessageHandler(() => _sms);
            });
        });
    }

    [Fact]
    public async Task ReadingsAsync_ProductionBaseAddress_KeepsTheV2Path()
    {
        using var scope = _factory.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<MeterVendorClient>().ReadingsAsync("SN 1", CancellationToken.None);

        _vendor.Requests.Should().ContainSingle().Which.Should().Be("https://meters.example.net/v2/meters/SN%201/readings");
    }

    [Fact]
    public async Task SendAsync_GatewayError_IsNotRetried()
    {
        using var scope = _factory.Services.CreateScope();

        var send = () => scope.ServiceProvider.GetRequiredService<SmsClient>().SendAsync("+966500000000", "x", CancellationToken.None);

        await send.Should().ThrowAsync<HttpRequestException>();
        _sms.Requests.Should().ContainSingle().Which.Should().Be("https://sms.example.net/messages");
    }

    [Fact]
    public void JwtBearer_Always_ValidatesTheMeterSyncAudience()
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.TokenValidationParameters.ValidAudience.Should().Be("meter-sync");
    }

    [Fact]
    public async Task Preflight_UnknownOrigin_GetsNoCorsHeaders()
    {
        var response = await Preflight("https://evil.example.com");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_ClinicOrigin_IsAllowed()
    {
        var response = await Preflight("https://clinic.example.net");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://clinic.example.net");
    }

    [Fact]
    public async Task HealthLive_Anonymous_ReturnsOk() =>
        (await _factory.CreateClient().GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task ListMeters_NoToken_ReturnsUnauthorized() =>
        (await _factory.CreateClient().GetAsync("/meters")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    public void Dispose() => _factory.Dispose();

    private async Task<HttpResponseMessage> Preflight(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return await _factory.CreateClient().SendAsync(request);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!.AbsoluteUri);
            }

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services, Type implementation)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T) && d.ImplementationType == implementation).ToList())
        {
            services.Remove(d);
        }
    }
}
