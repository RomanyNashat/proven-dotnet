using System.Net;
using System.Security.Claims;
using System.Text;
using ClinicQueue.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ClinicQueue.Tests;

/// <summary>The service as the cluster runs it: Production, every request from nginx's pod, tokens we sign.</summary>
public sealed class ServiceFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.example.net/realms/staff";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes(new string('k', 64))) { KeyId = "test-key" };

    public static string TokenFor(int patientId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "clinic-queue",
        Subject = new ClaimsIdentity([new Claim("sub", patientId.ToString(System.Globalization.CultureInfo.InvariantCulture))]),
        Expires = DateTime.UtcNow.AddMinutes(5),
        SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClinicSource>();
            services.AddScoped<IClinicSource>(_ => new FixedClinicSource());
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(Key);
                o.Configuration = configuration;
                o.ConfigurationManager = null;
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = Key;
            });
            services.AddSingleton<IStartupFilter, FromNginxPod>();
        });
    }

    private sealed class FixedClinicSource : IClinicSource
    {
        public IReadOnlyList<ClinicEntry> Load() => [new ClinicEntry(1, "North"), new ClinicEntry(2, "South")];
    }

    private sealed class FromNginxPod : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, nextMiddleware) =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse("10.42.0.7");
                return nextMiddleware(http);
            });
            next(app);
        };
    }
}
