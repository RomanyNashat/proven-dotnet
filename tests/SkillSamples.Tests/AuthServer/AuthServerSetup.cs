using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SkillSamples.AuthServer;

public static class AuthServerSetup
{
    public const string OrdersApi = "orders-api";

    // keys: real certificates from the secret store in production; ephemeral keys only in tests.
    public static IServiceCollection AddAuthServer(
        this IServiceCollection services, string connectionString, Action<OpenIddictServerBuilder> keys)
    {
        // bigint keys for OpenIddict's tables: the token table grows fastest.
        services.AddDbContext<AuthDbContext>(o => o.UseSqlServer(connectionString).UseOpenIddict<long>());

        services.AddIdentity<AppUser, AppRole>(o =>
            {
                o.Password.RequiredLength = 12;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.User.RequireUniqueEmail = true;
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version2;   // no passkey table (binary columns)
                o.Stores.MaxLengthForKeys = 128;                             // composite keys stay under 900 bytes
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        services.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<AuthDbContext>().ReplaceDefaultEntities<long>())
            .AddServer(o =>
            {
                o.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetIntrospectionEndpointUris("connect/introspect")
                    .SetRevocationEndpointUris("connect/revoke")
                    .SetEndSessionEndpointUris("connect/logout");

                o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                o.AllowRefreshTokenFlow();
                o.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess, "orders");

                // Opaque access tokens: services introspect them, and a revoked token is refused at once.
                o.UseReferenceAccessTokens().UseReferenceRefreshTokens();
                o.SetAccessTokenLifetime(TimeSpan.FromMinutes(15)).SetRefreshTokenLifetime(TimeSpan.FromDays(14));

                keys(o);

                o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()     // so a refresh reloads the user's roles
                    .EnableEndSessionEndpointPassthrough();
            });

        return services;
    }
}
