using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace SkillSamples.Auth;

public sealed class InMemoryVisits : IVisitStore
{
    public Task<VisitRecord?> FindAsync(int id, CancellationToken ct) =>
        Task.FromResult(id == 1 ? new VisitRecord(1, "patient-a", "follow-up") : null);
}

public sealed class AuthTests
{
    private const string Issuer = "https://idp.test/realms/clinic";
    private const string Audience = "visits-api";
    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

    private static string Token(
        string sub, object? realmRoles = null, object? clientRoles = null, string audience = Audience, TimeSpan? expiresIn = null)
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var expires = now + (expiresIn ?? TimeSpan.FromMinutes(5));
        var claims = new Dictionary<string, object> { ["sub"] = sub, ["preferred_username"] = $"user-{sub}" };
        if (realmRoles is not null)
        {
            claims["realm_access"] = new Dictionary<string, object> { ["roles"] = realmRoles };
        }

        if (clientRoles is not null)
        {
            claims["resource_access"] = new Dictionary<string, object> { [Audience] = new Dictionary<string, object> { ["roles"] = clientRoles } };
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = expires.AddMinutes(-10),
            NotBefore = expires.AddMinutes(-10),
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
        });
    }

    /// <param name="skillsOldConfig">The shape the skill used to show: RoleClaimType "realm_access.roles", no role mapping.</param>
    private static async Task<WebApplication> StartAsync(bool skillsOldConfig = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Authority"] = Issuer, ["Auth:Audience"] = Audience });

        if (skillsOldConfig)
        {
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
            {
                o.Audience = Audience;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters { NameClaimType = "preferred_username", RoleClaimType = "realm_access.roles" };
            });
        }
        else
        {
            builder.Services.AddKeycloakJwt(builder.Configuration);
        }

        // Tests sign their own tokens: no metadata download, the test key and issuer instead. Configure, not
        // PostConfigure: JwtBearer's own post-configure would already have set up the metadata download.
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.Authority = null;
            o.RequireHttpsMetadata = false;
            o.TokenValidationParameters.ValidIssuer = Issuer;
            o.TokenValidationParameters.IssuerSigningKey = Key;
        });

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("AdminOnly", p => p.RequireRole("admin"));
        builder.Services.AddSingleton<IAuthorizationHandler, VisitAccessHandler>();
        builder.Services.AddSingleton<IVisitStore, InMemoryVisits>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => "ok").AllowAnonymous();
        app.MapGet("/me", (ClaimsPrincipal user) => user.Identity!.Name);
        app.MapGet("/admin", () => "admin area").RequireAuthorization("AdminOnly");
        app.MapGet("/visits/{id:int}", VisitEndpoints.GetVisit);
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> GetAsync(WebApplication app, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await app.GetTestClient().SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task KeycloakRealmRole_WithTheOldRoleClaimType_IsNeverSeen()
    {
        await using var app = await StartAsync(skillsOldConfig: true);

        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(app, "/admin", Token("u1", realmRoles: new[] { "admin" })));
    }

    [Fact]
    public async Task KeycloakRealmAndClientRoles_AreMappedToRoles()
    {
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/admin", Token("u1", realmRoles: new[] { "admin" })));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/admin", Token("u2", clientRoles: new[] { "admin" })));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(app, "/admin", Token("u3", realmRoles: new[] { "nurse" })));
    }

    [Fact]
    public async Task FallbackPolicy_NoTokenIs401_AllowAnonymousStillAnswers()
    {
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/me", token: null));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/health", token: null));
    }

    [Fact]
    public async Task Token_ForAnotherAudience_Is401()
    {
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/me", Token("u1", audience: "account")));   // Keycloak's default audience
    }

    [Fact]
    public async Task ExpiredToken_IsAcceptedOnlyWithinTheThirtySecondSkew()
    {
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/me", Token("u1", expiresIn: TimeSpan.FromSeconds(-10))));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/me", Token("u1", expiresIn: TimeSpan.FromSeconds(-90))));
    }

    [Fact]
    public async Task Name_IsThePreferredUsername()
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("u1"));

        using var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal("user-u1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Visit_OwnerAndDoctorSeeIt_AnyoneElseGetsNotFound()
    {
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/visits/1", Token("patient-a")));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/visits/1", Token("dr-x", realmRoles: new[] { "doctor" })));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(app, "/visits/1", Token("patient-b")));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(app, "/visits/2", Token("patient-a")));
    }

    private sealed class CountingTokenClient : IServiceTokenClient
    {
        public int Requests { get; private set; }

        public Task<ServiceToken> RequestAsync(string audience, CancellationToken ct) =>
            Task.FromResult(new ServiceToken($"token-{++Requests}", TimeSpan.FromSeconds(2)));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string?> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public void ServiceTokenHandler_AddedByTypeWithoutRegistration_FailsWhenTheClientIsCreated()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("inventory").AddHttpMessageHandler<ServiceTokenHandler>();
        using var sp = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IHttpClientFactory>().CreateClient("inventory"));
    }

    [Fact]
    public async Task ServiceTokenHandler_ReusesTheTokenUntilItNearlyExpires()
    {
        var tokens = new CountingTokenClient();
        var capture = new CaptureHandler();
        var services = new ServiceCollection().AddMemoryCache().AddSingleton<IServiceTokenClient>(tokens);
        services.AddHttpClient("inventory", c => c.BaseAddress = new Uri("https://inventory.test/"))
            .AddHttpMessageHandler(sp => new ServiceTokenHandler(sp.GetRequiredService<IServiceTokenClient>(), sp.GetRequiredService<IMemoryCache>(), "inventory"))
            .ConfigurePrimaryHttpMessageHandler(() => capture);
        await using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("inventory");

        await client.GetAsync("stock");
        await client.GetAsync("stock");
        await Task.Delay(TimeSpan.FromSeconds(1.5));   // the 2 s token is refreshed at half its life
        await client.GetAsync("stock");

        Assert.Equal(new[] { "token-1", "token-1", "token-2" }, capture.Seen);
    }
}
