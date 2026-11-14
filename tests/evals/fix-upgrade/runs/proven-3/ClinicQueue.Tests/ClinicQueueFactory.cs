using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using ClinicQueue.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ClinicQueue.Tests;

/// <summary>
/// Runs the service as the cluster does: Production, every request from nginx's pod, a fixed clinic list
/// instead of the database, and tokens signed here instead of by the identity server.
/// </summary>
public sealed class ClinicQueueFactory : WebApplicationFactory<Program>
{
    public static readonly IPAddress NginxPod = IPAddress.Parse("10.42.0.7");
    private const string Issuer = "https://id.test";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("clinic-queue-tests-signing-key-32bytes!!"));

    public static IReadOnlyList<ClinicEntry> Clinics { get; } = [new ClinicEntry(1, "North"), new ClinicEntry(2, "South")];

    public static string TokenFor(string subject)
    {
        // Token lifetimes are checked against the real clock by the JWT handler, so the token uses it too.
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var token = new JwtSecurityToken(
            Issuer,
            "clinic-queue",
            [new Claim("sub", subject)],
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Queue", "Host=localhost;Database=none;Username=none");
        builder.UseSetting("Auth:Authority", Issuer);
        builder.UseSetting("Proxies:Network", "10.42.0.0/16");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClinicSource>();
            services.AddSingleton<IClinicSource>(new FixedClinicSource(Clinics));
            services.AddSingleton<IStartupFilter, FromNginxPod>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.Configuration.SigningKeys.Add(SigningKey);
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
        });
    }

    private sealed class FixedClinicSource(IReadOnlyList<ClinicEntry> clinics) : IClinicSource
    {
        public IReadOnlyList<ClinicEntry> Load() => clinics;
    }

    private sealed class FromNginxPod : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, call) =>
            {
                http.Connection.RemoteIpAddress = NginxPod;
                return call(http);
            });
            next(app);
        };
    }
}
