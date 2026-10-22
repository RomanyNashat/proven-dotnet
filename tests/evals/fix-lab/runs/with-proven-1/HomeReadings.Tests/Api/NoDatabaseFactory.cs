using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HomeReadings.Tests.Api;

/// <summary>
/// The service in Production, with a database that refuses every connection (nothing here runs
/// PostgreSQL) and a care team answered by <see cref="CareTeam"/>. Tokens are signed with a test key.
/// </summary>
public sealed class NoDatabaseFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://tests.local/";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("home-readings-tests-signing-key-0123456789"));

    public string PhotoRoot { get; } = Path.Combine(Path.GetTempPath(), "home-readings-tests", Guid.NewGuid().ToString("N"));

    public RecordingHandler CareTeam { get; } = new(HttpStatusCode.ServiceUnavailable);

    public static string TokenFor(string subject) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "home-readings",
        Subject = new ClaimsIdentity([new Claim("sub", subject)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
    });

    public HttpClient ClientFor(string subject)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TokenFor(subject));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Readings", "Host=127.0.0.1;Port=1;Database=readings;Username=readings;Password=x;Timeout=3");
        builder.UseSetting("CareTeam:BaseAddress", "http://careteam.tests.local/api/");
        builder.UseSetting("Storage:PhotoRoot", PhotoRoot);
        builder.UseSetting("Auth:Authority", Issuer);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Authority = null;
                o.RequireHttpsMetadata = false;
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = Key;
            });
            services.AddHttpClient<HomeReadings.Infrastructure.CareTeamClient>().ConfigurePrimaryHttpMessageHandler(() => CareTeam);
        });
    }
}

/// <summary>Answers every request with one status and counts the calls.</summary>
public sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new HttpResponseMessage(status));
    }
}
