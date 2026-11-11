using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MeterSync.Application;
using Xunit;

namespace MeterSync.Tests;

// Nothing here touches the database; the background worker is removed so it doesn't try to.
public sealed class HostTests : IClassFixture<HostTests.Factory>
{
    private readonly Factory _factory;

    public HostTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task HealthLive_Anonymous_ReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMeters_NoToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("https://clinic.example.net", true)]
    [InlineData("https://evil.example.com", false)]
    public async Task Cors_Origin_AllowedOnlyWhenConfigured(string origin, bool allowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _factory.CreateClient().SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().Be(allowed);
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public void Services_PatientDirectory_IsScoped()
    {
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        scope1.ServiceProvider.GetRequiredService<MeterSync.Infrastructure.PatientDirectory>()
            .Should().NotBeSameAs(scope2.ServiceProvider.GetRequiredService<MeterSync.Infrastructure.PatientDirectory>());
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(s =>
            {
                var worker = s.Single(d => d.ImplementationType == typeof(SyncWorker));
                s.Remove(worker);
            });
        }
    }
}
