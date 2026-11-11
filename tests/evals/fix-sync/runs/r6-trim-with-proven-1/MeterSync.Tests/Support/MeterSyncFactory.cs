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
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MeterSync.Tests.Support;

/// <summary>
/// Runs the real Program in the Production environment, with the background worker removed and a local
/// signing key in place of the identity provider's metadata. There is no PostgreSQL here, so only paths that
/// answer before touching the database (or that prove the database error is hidden) are tested through it.
/// </summary>
public sealed class MeterSyncFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://issuer.test";
    public const string Audience = "meter-sync";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("a-test-only-signing-key-of-at-least-32-bytes!"));

    public static string Token(string? sub = "1", string audience = Audience)
    {
        var claims = new Dictionary<string, object>();
        if (sub is not null)
        {
            claims[JwtRegisteredClaimNames.Sub] = sub;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            Subject = new ClaimsIdentity(),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureTestServices(services =>
        {
            var worker = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(SyncWorker));
            services.Remove(worker);

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration { Issuer = Issuer });
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
        });
    }
}
