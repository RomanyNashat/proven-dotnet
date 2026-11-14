using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using ClinicQueue.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ClinicQueue.Tests;

/// <summary>
/// Runs the service the way the cluster does, minus PostgreSQL: Production, every request from nginx's pod
/// (10.42.0.7), tokens signed by the test. Anything that would reach the database is replaced.
/// </summary>
public sealed class ClinicQueueFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.test/realms/staff";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("test-signing-key-that-is-long-enough-for-hs256"));

    public IClinicSource ClinicSource { get; init; } = new FixedClinicSource([]);

    public IDistributedCache? Cache { get; init; }

    public CapturingLoggerProvider Logs { get; } = new();

    public static string TokenFor(int patientId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "clinic-queue",
        Subject = new ClaimsIdentity([new Claim("sub", patientId.ToString(System.Globalization.CultureInfo.InvariantCulture))]),
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
    });

    public HttpClient CreateAuthenticatedClient(int patientId = 7)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(patientId));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Queue", "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1");
        builder.UseSetting("Auth:Authority", Issuer);
        builder.UseSetting("Proxies:Network", "10.42.0.0/16");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped(_ => ClinicSource);
            if (Cache is not null)
            {
                services.AddSingleton(Cache);
            }

            services.AddSingleton<IStartupFilter, FromNginxPod>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.Configuration.SigningKeys.Add(SigningKey);
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
                o.TokenValidationParameters.ValidIssuer = Issuer;
            });
        });
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

public sealed class FixedClinicSource(IReadOnlyList<ClinicEntry> clinics, TimeSpan? latency = null) : IClinicSource
{
    public IReadOnlyList<ClinicEntry> Load()
    {
        if (latency is { } wait)
        {
            // Load is synchronous (the contract), so a slow database round trip is a blocking wait.
            using var never = new ManualResetEventSlim(false);
            never.Wait(wait);
        }

        return clinics;
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Category, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<(LogLevel, string, Exception?)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, category, exception));
    }
}
