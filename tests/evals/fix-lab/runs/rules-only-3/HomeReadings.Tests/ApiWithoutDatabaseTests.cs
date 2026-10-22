using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using HomeReadings.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HomeReadings.Tests;

/// <summary>The service in-process with no database: what it does before it touches PostgreSQL.</summary>
public sealed class ApiWithoutDatabaseTests : IClassFixture<ApiWithoutDatabaseTests.App>
{
    private readonly App _app;

    public ApiWithoutDatabaseTests(App app) => _app = app;

    [Fact]
    public async Task Live_DatabaseUnreachable_IsHealthy()
    {
        var response = await _app.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readings_NoToken_Unauthorized()
    {
        var response = await _app.CreateClient().GetAsync("/readings/today");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Readings_SubIsNotAPatientId_Forbidden()
    {
        var response = await _app.Client("not-a-number").GetAsync("/readings/today");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostReading_ValueNotANumber_BadRequestNamingValue()
    {
        var response = await _app.Client("42").PostAsJsonAsync("/readings",
            new { nationalId = "1012345678", value = "abc", unit = "mmol/L", collectedAt = "2026-10-22T08:00:00+03:00", notes = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").TryGetProperty("value", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Today_DatabaseFails_ProblemWithoutExceptionText()
    {
        var client = _app.Client("42");
        client.DefaultRequestHeaders.Add("x-language", "ar");

        var response = await client.GetAsync("/readings/today");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("ConnectionString").And.NotContain("Exception");
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().Should().Be(Messages.SomethingWentWrong("ar"));
    }

    public sealed class App : WebApplicationFactory<Program>
    {
        private const string Issuer = "https://issuer.test";
        private static readonly SymmetricSecurityKey Key = new(new byte[32]);

        public HttpClient Client(string sub)
        {
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = "home-readings",
                Subject = new ClaimsIdentity([new Claim("sub", sub)]),
                Expires = DateTime.UtcNow.AddMinutes(10),
                SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
            });
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Storage:PhotoRoot", Path.GetTempPath());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    o.TokenValidationParameters.ValidIssuer = Issuer;
                    o.TokenValidationParameters.IssuerSigningKey = Key;
                });
            });
        }
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
