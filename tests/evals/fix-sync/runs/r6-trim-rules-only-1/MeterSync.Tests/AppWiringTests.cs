using System.Net;
using FluentAssertions;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace MeterSync.Tests;

/// <summary>Runs the app in-process, in Production, without touching the database (the worker is removed).</summary>
public sealed class AppWiringTests : IClassFixture<AppWiringTests.Factory>
{
    private readonly Factory _factory;

    public AppWiringTests(Factory factory) => _factory = factory;

    [Fact]
    public void HttpClients_VendorBaseAddress_KeepsVersionPath()
    {
        var vendor = _factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MeterVendorClient));

        new Uri(vendor.BaseAddress!, "meters/A1/readings").Should().Be(new Uri("https://meters.example.net/v2/meters/A1/readings"));
    }

    [Fact]
    public void HttpClients_Sms_HasFiniteTimeout()
    {
        var sms = _factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SmsClient));

        sms.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void JwtOptions_Default_ValidatesMeterSyncAudience()
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.TokenValidationParameters.ValidAudience.Should().Be("meter-sync");
    }

    [Fact]
    public void Services_FromScope_ResolveWithoutCaptiveDependencies()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<MeterSyncService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<AlertSweeper>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<PatientDirectory>().Should().NotBeNull();
    }

    [Theory]
    [InlineData("https://clinic.example.net", true)]
    [InlineData("https://evil.example.com", false)]
    public async Task Cors_Preflight_AllowsOnlyClinic(string origin, bool allowed)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().Be(allowed);
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task GetMeters_NoToken_Returns401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_Anonymous_Returns200()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                var worker = services.Single(d => d.ImplementationType == typeof(SyncWorker));
                services.Remove(worker);
            });
            builder.UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = true;
            });
        }
    }
}
