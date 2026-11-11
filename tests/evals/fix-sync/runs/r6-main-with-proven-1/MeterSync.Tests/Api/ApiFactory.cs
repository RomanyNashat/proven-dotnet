using System.Security.Claims;
using System.Text;
using MeterSync.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MeterSync.Tests.Api;

/// <summary>Runs the app in Production with the worker stopped and locally signed tokens. No database is touched.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://issuer.test";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("test-signing-key-test-signing-key-0123456789"));

    public static string TokenFor(string? sub, string audience = "meter-sync")
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
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.ConfigureServices(services =>
        {
            var worker = services.Single(d => d.ImplementationType == typeof(SyncWorker));
            services.Remove(worker);

            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(Key);
                o.Configuration = configuration;
            });
        });
    }
}
