using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using ClinicQueue.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ClinicQueue.Tests;

/// <summary>Runs the service as production does (Production, behind nginx at 10.42.0.7), with no database.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.test/realms/staff";
    private static readonly SymmetricSecurityKey SigningKey =
        new(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key" };

    public static IReadOnlyList<ClinicEntry> Clinics { get; } = [new(1, "North"), new(2, "South")];

    public CapturingLoggerProvider Logs { get; } = new();

    public HttpClient CreateClientAs(int patientId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(patientId));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Queue", "Host=invalid.invalid;Database=queue;Username=queue;Timeout=1");
        builder.UseSetting("Auth:Authority", Issuer);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClinicSource>();
            services.AddScoped<IClinicSource>(_ => new FixedClinicSource(Clinics));
            services.AddSingleton<IStartupFilter, FromNginxPod>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                // The service's own post-configure has already built a metadata manager for the authority;
                // replace it so no discovery request goes out.
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                o.Configuration = configuration;
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }

    private static string Token(int patientId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "clinic-queue",
        Subject = new ClaimsIdentity([new Claim("sub", patientId.ToString(System.Globalization.CultureInfo.InvariantCulture))]),
        Expires = DateTime.UtcNow.AddMinutes(5),
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
    });

    private sealed class FixedClinicSource(IReadOnlyList<ClinicEntry> clinics) : IClinicSource
    {
        public IReadOnlyList<ClinicEntry> Load() => clinics;
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
