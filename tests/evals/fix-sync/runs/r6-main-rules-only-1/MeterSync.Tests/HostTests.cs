using System.Net;
using FluentAssertions;
using MeterSync.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MeterSync.Tests;

// Endpoints that don't touch the database; the background worker is removed so nothing tries to connect.
public sealed class HostTests : IClassFixture<HostTests.Factory>
{
    private readonly Factory _factory;

    public HostTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task HealthLive_Anonymous_ReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    [Fact]
    public async Task GetMeters_NoToken_ReturnsUnauthorized() =>
        (await _factory.CreateClient().GetAsync("/meters")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("https://clinic.example.net", true)]
    [InlineData("https://evil.example.com", false)]
    public async Task Preflight_Origin_AllowedOnlyForClinic(string origin, bool allowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _factory.CreateClient().SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().Be(allowed);
    }

    [Fact]
    public void Services_FromScope_ResolveSyncAndSweeper()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<MeterSyncService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<AlertSweeper>().Should().NotBeNull();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(s => s.RemoveAll<IHostedService>());
        }
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(d);
        }
    }
}
