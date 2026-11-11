using System.Security.Claims;
using MeterSync.Application;
using MeterSync.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MeterSync.Tests;

/// <summary>The service in Production, with no database, no background worker and locally signed tokens.</summary>
public sealed class MeterSyncFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://test-issuer.example";
    private static readonly SymmetricSecurityKey SigningKey = new(new byte[32]);

    public StubHandler Vendor { get; } = new();

    public StubHandler Sms { get; } = new();

    public static string Token(string audience = "meter-sync", string? sub = "42")
    {
        var claims = new List<Claim>();
        if (sub is not null)
        {
            claims.Add(new Claim("sub", sub));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Sync", TestDb.DummyConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(SyncWorker)));
            services.AddHttpClient<MeterSync.Infrastructure.MeterVendorClient>().ConfigurePrimaryHttpMessageHandler(() => Vendor);
            services.AddHttpClient<MeterSync.Infrastructure.SmsClient>().ConfigurePrimaryHttpMessageHandler(() => Sms);
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Authority = null;
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
        });
    }
}
