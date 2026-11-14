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

/// <summary>The service as the cluster runs it, minus the database: Production, behind nginx at 10.42.0.7.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.example.net/realms/staff";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("test-signing-key-test-signing-key-0123456789"));

    public static string TokenFor(int patientId)
    {
        var token = new JwtSecurityToken(
            Issuer,
            "clinic-queue",
            [new Claim("sub", patientId.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClinicSource>();
            services.AddScoped<IClinicSource, FixedClinicSource>();
            services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, FromNginxStartupFilter>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(Key);
                o.Configuration = configuration;
                o.TokenValidationParameters.IssuerSigningKey = Key;
                o.TokenValidationParameters.ValidIssuer = Issuer;
            });
        });
    }

    private sealed class FromNginxStartupFilter : IStartupFilter
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
