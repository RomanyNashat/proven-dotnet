using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HomeReadings.Tests.Support;

/// <summary>
/// The service in-process, in Production, with a database that refuses connections (there is no
/// PostgreSQL here). Only behaviour that is decided before the database is reached can be tested.
/// </summary>
public sealed class ServiceFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.example.test/realms/patients";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("test-signing-key-that-is-long-enough-for-hs256"));

    public string PhotoRoot { get; } = Path.Combine(AppContext.BaseDirectory, "service-photos", Guid.NewGuid().ToString("N"));

    public HttpClient CreatePatientClient(string sub = "42")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(sub));
        return client;
    }

    public static string TokenFor(string sub) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "home-readings",
        Subject = new ClaimsIdentity([new Claim("sub", sub)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Readings", "Host=127.0.0.1;Port=1;Database=readings;Username=readings;Password=x;Timeout=2");
        builder.UseSetting("Auth:Authority", Issuer);
        builder.UseSetting("CareTeam:BaseAddress", "https://careteam.example.test/api/");
        builder.UseSetting("Storage:PhotoRoot", PhotoRoot);
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
                o.TokenValidationParameters.ValidIssuer = Issuer;
            });
        });
    }
}
