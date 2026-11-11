using System.Net;
using System.Security.Claims;
using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MeterSync.Tests;

public sealed class WiringTests : IClassFixture<WiringTests.Factory>
{
    private readonly Factory _factory;

    public WiringTests(Factory factory) => _factory = factory;

    [Fact]
    public void BaseAddress_WithoutTrailingSlash_KeepsVersionSegment()
    {
        var uri = new Uri(BaseAddress.From("https://meters.example.net/v2", "k"), "meters/abc/readings");

        uri.AbsoluteUri.Should().Be("https://meters.example.net/v2/meters/abc/readings");
    }

    [Fact]
    public void VendorClient_ProductionSettings_KeepsVersionSegment()
    {
        var http = _factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MeterVendorClient));

        http.BaseAddress!.AbsoluteUri.Should().Be("https://meters.example.net/v2/");
    }

    [Fact]
    public void JwtBearer_ProductionSettings_ValidatesAudience()
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        options.TokenValidationParameters.Should().BeEquivalentTo(new { ValidateAudience = true, ValidAudience = "meter-sync" });
    }

    [Fact]
    public void Services_ResolvedFromScope_HaveNoCaptiveDbContext()
    {
        using var first = _factory.Services.CreateScope();
        using var second = _factory.Services.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<PatientDirectory>();
        var b = second.ServiceProvider.GetRequiredService<PatientDirectory>();

        a.Should().NotBeSameAs(b, "a singleton would share one DbContext across requests");
    }

    [Fact]
    public async Task GetMeters_NoToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/meters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_NoToken_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

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

    [Theory]
    [InlineData("42", 42)]
    [InlineData("abc", null)]
    [InlineData("-1", null)]
    public void CurrentPatientId_Sub_ParsesOnlyPositiveInts(string sub, int? expected) =>
        CurrentPatient.Id(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)]))).Should().Be(expected);

    [Fact]
    public void Model_CreateScript_MatchesRawSqlColumnsAndBoundsStrings()
    {
        using var scope = _factory.Services.CreateScope();
        var script = scope.ServiceProvider.GetRequiredService<SyncDbContext>().Database.GenerateCreateScript();

        script.Should().Contain("\"AlertState\" character varying(12)")
            .And.Contain("\"ValueMgDl\" numeric(5,1)")
            .And.Contain("REFERENCES meters")
            .And.NotContain(" text");
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
