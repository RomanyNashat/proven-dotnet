using System.IdentityModel.Tokens.Jwt;
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
using Microsoft.IdentityModel.Tokens;

namespace ClinicQueue.Tests;

/// <summary>
/// The service as the cluster runs it, minus PostgreSQL: Production environment, every request arriving from
/// nginx's pod, tokens signed locally, and the clinic list from a test source.
/// </summary>
public sealed class ServiceFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.test/realms/staff";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("a-test-signing-key-of-at-least-32-bytes!"));

    public static readonly IReadOnlyList<ClinicEntry> Clinics = [new(1, "North"), new(2, "South")];

    public static IPAddress NginxPod { get; } = IPAddress.Parse("10.42.0.7");

    public static string TokenFor(int patientId)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: "clinic-queue",
            claims: [new Claim("sub", patientId.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            expires: TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(5),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Queue", "Host=127.0.0.1;Port=1;Database=none;Username=none");
        builder.UseSetting("Auth:Authority", Issuer);
        builder.UseSetting("Proxies:Network", "10.42.0.0/16");
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IClinicSource, SlowClinicSource>();
            services.AddTransient<IStartupFilter, FromNginxPod>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.ConfigurationManager = null;
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
        });
    }

    /// <summary>Slow on purpose, so a warm-up that doesn't hold up start-up is caught.</summary>
    private sealed class SlowClinicSource : IClinicSource
    {
        public IReadOnlyList<ClinicEntry> Load()
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(500));
            return Clinics;
        }
    }

    private sealed class FromNginxPod : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, nextMiddleware) =>
            {
                http.Connection.RemoteIpAddress = NginxPod;
                return nextMiddleware(http);
            });
            next(app);
        };
    }
}
