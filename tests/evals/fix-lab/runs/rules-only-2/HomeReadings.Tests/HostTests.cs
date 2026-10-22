using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HomeReadings.Tests;

/// <summary>The host in Production with no database: what can be checked without PostgreSQL.</summary>
public sealed class HostTests : IClassFixture<HostTests.Factory>
{
    private const string SigningKey = "unit-test-signing-key-that-is-long-enough-for-hs256";
    private readonly Factory _factory;

    public HostTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task HealthLive_NoDatabase_IsHealthyWithoutToken()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/readings/latest")]
    [InlineData("/readings/today")]
    [InlineData("/readings/1")]
    public async Task Readings_NoToken_Unauthorized(string path) =>
        (await _factory.CreateClient().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Readings_SubIsNotAPatientId_Forbidden()
    {
        var client = Client("not-a-number");

        (await client.GetAsync("/readings/latest")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostReading_UnreadableValue_ValidationProblemWithoutDetailsLeak()
    {
        var client = Client("42");

        var response = await client.PostAsJsonAsync("/readings", new
        {
            nationalId = "1000000001",
            value = "HI",
            unit = "mmol/L",
            collectedAt = "2026-10-22T08:00:00+03:00",
            notes = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"value\"");
    }

    [Fact]
    public void Services_ResolveWhatTheTestsUse()
    {
        _factory.Services.GetRequiredService<ReadingNotifier>().Should().NotBeNull();
        _factory.Services.GetRequiredService<TimeProvider>().Should().NotBeNull();
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CareTeamClient>().Should().NotBeNull();
    }

    [Fact]
    public void Model_BoundsTextAndKeepsOneReadingAndOneAlert()
    {
        using var scope = _factory.Services.CreateScope();
        var script = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>().Database.GenerateCreateScript();

        script.Should().NotContain(" text").And.Contain("character varying(500)");
        script.Should().Contain("CREATE UNIQUE INDEX \"IX_readings_PatientId_CollectedAt_Value_Unit\"");
        script.Should().Contain("CREATE UNIQUE INDEX \"IX_care_team_alerts_ReadingId\"");
    }

    private HttpClient Client(string sub)
    {
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "test",
            audience: "home-readings",
            claims: [new Claim("sub", sub)],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256)));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Storage:PhotoRoot", Path.Combine(Path.GetTempPath(), "home-readings-tests"));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.Authority = null;
                    o.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();
                    o.TokenValidationParameters.ValidIssuer = "test";
                    o.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
                }));
        }
    }
}
