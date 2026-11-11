using System.Security.Claims;
using System.Text;
using MeterSync.Application;
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

/// <summary>Runs the service in Production, without the background worker and with locally signed tokens.</summary>
public sealed class MeterSyncFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://test-issuer.example";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("test-signing-key-that-is-long-enough-for-hs256!"));

    public static string TokenFor(string sub, string audience = "meter-sync") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", sub)]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureTestServices(services =>
        {
            var worker = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(SyncWorker));
            services.Remove(worker);

            // Configure (not PostConfigure) so the JwtBearer post-configure sees a static configuration and never fetches metadata.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var config = new OpenIdConnectConfiguration { Issuer = Issuer };
                config.SigningKeys.Add(SigningKey);
                o.Configuration = config;
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
        });
    }
}
