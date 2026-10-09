using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SkillSamples.Auth;

public sealed class PortalAuthDbContext(DbContextOptions<PortalAuthDbContext> options) : DbContext(options);

/// <summary>
/// A real OpenIddict server for the tests, on Kestrel, its store on SQL Server (openiddict-server skill).
/// Client credentials only: "admin-tool" gets the admin role, "reports-job" none, "visits-api" introspects.
/// </summary>
public static class PortalServer
{
    public const string Secret = "test-secret-not-for-production";

    public static async Task<WebApplication> StartAsync(string connectionString, bool encryptAccessTokens, bool referenceTokens)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<PortalAuthDbContext>(o =>
        {
            o.UseSqlServer(connectionString);
            o.UseOpenIddict();
        });
        builder.Services.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<PortalAuthDbContext>())
            .AddServer(o =>
            {
                o.SetTokenEndpointUris("connect/token")
                    .SetIntrospectionEndpointUris("connect/introspect")
                    .SetRevocationEndpointUris("connect/revoke");
                o.AllowClientCredentialsFlow();
                o.RegisterScopes("visits");
                o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
                if (!encryptAccessTokens)
                {
                    o.DisableAccessTokenEncryption();
                }

                if (referenceTokens)
                {
                    o.UseReferenceAccessTokens();
                }

                o.UseAspNetCore().EnableTokenEndpointPassthrough().DisableTransportSecurityRequirement();
            });

        var app = builder.Build();
        app.MapPost("connect/token", IssueAsync);
        await CreateSchemaAndClientsAsync(app.Services);
        await app.StartAsync();
        return app;
    }

    public static Uri Address(WebApplication app) => new(app.Urls.First() + "/");

    private static IResult IssueAsync(HttpContext http)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenIddict request.");
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, request.ClientId);
        if (request.ClientId == "admin-tool")
        {
            identity.SetClaims(Claims.Role, ["admin"]);
        }

        identity.SetScopes(request.GetScopes());
        identity.SetResources("visits-api");
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task CreateSchemaAndClientsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalAuthDbContext>();
        var exists = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'OpenIddictApplications'").SingleAsync();
        if (exists == 0)
        {
            foreach (var batch in Regex.Split(db.Database.GenerateCreateScript(), @"^\s*GO\s*$", RegexOptions.Multiline))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await db.Database.ExecuteSqlRawAsync(batch);
                }
            }
        }

        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var (clientId, permissions) in new[]
        {
            ("admin-tool", ClientPermissions),
            ("reports-job", ClientPermissions),
            ("visits-api", new[] { Permissions.Endpoints.Introspection }),
        })
        {
            if (await apps.FindByClientIdAsync(clientId) is null)
            {
                var descriptor = new OpenIddictApplicationDescriptor { ClientId = clientId, ClientSecret = Secret, ClientType = ClientTypes.Confidential };
                descriptor.Permissions.UnionWith(permissions);
                await apps.CreateAsync(descriptor);
            }
        }
    }

    private static readonly string[] ClientPermissions =
    [
        Permissions.Endpoints.Token,
        Permissions.Endpoints.Revocation,
        Permissions.GrantTypes.ClientCredentials,
        Permissions.Prefixes.Scope + "visits",
    ];
}
