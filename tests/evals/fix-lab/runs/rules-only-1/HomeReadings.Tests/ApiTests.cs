using System.Net;
using System.Security.Claims;
using HomeReadings.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HomeReadings.Tests;

/// <summary>Runs the host in Production with no database reachable.</summary>
public sealed class ApiTests(ApiTests.Factory factory) : IClassFixture<ApiTests.Factory>
{
    [Fact]
    public async Task HealthLive_DatabaseDown_ReturnsOk()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_DatabaseDown_ReturnsServiceUnavailable()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/readings/1")]
    [InlineData("/readings/today")]
    [InlineData("/readings/latest")]
    public async Task Readings_NoToken_ReturnsUnauthorized(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("AR", true)]
    [InlineData("en", false)]
    [InlineData("", false)]
    public void IsArabic_Header_PicksLanguage(string header, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[AppLanguage.Header] = header;

        AppLanguage.IsArabic(context.Request).Should().Be(expected);
    }

    [Theory]
    [InlineData("17", true)]
    [InlineData("0", false)]
    [InlineData("8c1f6a0e-0000-0000-0000-000000000000", false)]
    public void TryGetPatientId_Sub_ParsesPositiveInt(string sub, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "test"));

        PatientClaims.TryGetPatientId(user, out _).Should().Be(expected);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Readings", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");
            builder.UseSetting("Storage:PhotoRoot", Path.Combine(Path.GetTempPath(), "home-readings-tests"));
        }
    }
}
