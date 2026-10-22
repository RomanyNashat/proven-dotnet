using System.Net.Http.Headers;
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
/// The service in-process, in Production, with a database that refuses connections (there is no
/// PostgreSQL here). Anything that must be decided before the database is touched can be tested.
/// </summary>
public sealed class HomeReadingsFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://id.test/realms/patients";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("test-signing-key-that-is-long-enough-for-hs256"));

    public string PhotoRoot { get; } = Path.Combine(Path.GetTempPath(), "home-readings-tests", Guid.NewGuid().ToString("N"), "photos");

    public CareTeamStub CareTeam { get; } = new();

    public HttpClient CreatePatientClient(string subject = "42", string? language = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(subject));
        if (language is not null)
        {
            client.DefaultRequestHeaders.Add("x-language", language);
        }

        return client;
    }

    public static string TokenFor(string subject) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "home-readings",
        Subject = new ClaimsIdentity([new Claim("sub", subject)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Readings", "Host=127.0.0.1;Port=1;Database=readings;Username=readings;Password=x;Timeout=2");
        builder.UseSetting("CareTeam:BaseAddress", "http://careteam.test/api/");
        builder.UseSetting("Storage:PhotoRoot", PhotoRoot);
        builder.UseSetting("Auth:Authority", Issuer);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
            services.AddHttpClient<HomeReadings.Infrastructure.CareTeamClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new CareTeamStubHandler(CareTeam));
        });
    }
}

/// <summary>Counts the calls the care team receives and answers with the status it is told to.</summary>
public sealed class CareTeamStub
{
    private int _calls;

    public int Calls => _calls;

    public System.Net.HttpStatusCode Status { get; set; } = System.Net.HttpStatusCode.OK;

    public void Record() => Interlocked.Increment(ref _calls);
}

public sealed class CareTeamStubHandler(CareTeamStub stub) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        stub.Record();
        return Task.FromResult(new HttpResponseMessage(stub.Status));
    }
}
