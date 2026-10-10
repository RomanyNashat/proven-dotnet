---
name: openiddict-server
description: Provider-side auth for .NET — your own OAuth2/OIDC server with OpenIddict and ASP.NET Core Identity — code flow with PKCE, opaque reference tokens, introspection, logout that really ends the tokens, roles as rows reloaded on refresh, lockout, a schema the DBA can review. Tested in CI against SQL Server. The issuing side; auth-patterns is the consuming side.
version: 2.0.0
---

# OpenIddict Server: when you are the token issuer

For a service that **issues** tokens: the OpenID Provider / authorization server. Services that only
**check** tokens (from this server or from an IdP such as Keycloak) follow `auth-patterns`.

**OpenIddict** (Apache-2.0, OpenID-certified) handles the protocol and the crypto; **ASP.NET Core
Identity** stores users, passwords and roles.

**On .NET 10, use OpenIddict 7.** OpenIddict 6 was built against EF Core 9, and EF Core 10 changed the
types behind `ExecuteUpdate`. Tested: with OpenIddict 6 on EF Core 10, logout failed with
`TypeLoadException: Could not load type 'SetPropertyCalls`1'` the moment it revoked tokens in bulk. Sign-in,
refresh and introspection still worked, so nothing looks wrong until someone logs out. A service that can't
move yet calls `.DisableBulkOperations()` on `UseEntityFrameworkCore()`: tokens are then revoked one by one. You write login, the authorize and token handlers, and
logout. The samples run on SQL Server and are tested end to end: a browser-like client signs in, runs
the code flow with PKCE, and an API introspects the token.

## Token model

**Opaque reference access tokens, checked by introspection.** The token is a random handle; its claims
stay on the server. Services call `/connect/introspect` (or use OpenIddict.Validation, `auth-patterns`
§2) to learn who it is and what roles they have. Revoking a token is a row update, so the next
introspection says `active: false`. The cost is a call per request; `auth-patterns` covers caching it and
what a cache does to revocation. The ID token is a JWT for the sign-in app only, never sent to APIs.

## Setup

<!-- sample: tests/SkillSamples.Tests/AuthServer/AuthServerSetup.cs -->
```csharp
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
                // Length over composition rules (NIST SP 800-63B): a long passphrase, no forced symbols.
                o.Password.RequiredLength = 12;
                o.Password.RequireUppercase = o.Password.RequireLowercase = false;
                o.Password.RequireDigit = o.Password.RequireNonAlphanumeric = false;
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
```

- **Keys:** production signs and encrypts with certificates from the secret store (`secret-management`),
  with a rotation plan: publish the new key before signing with it. `AddEphemeral...Key()` is for tests
  only; ephemeral keys change on every start.
- **HTTPS:** OpenIddict refuses plain HTTP. Behind an ingress that ends TLS, forward the scheme
  (`ForwardedHeaders`) rather than calling `DisableTransportSecurityRequirement()`, which the tests use.
- **Schema:** applied as a reviewed script, never `Migrate()` at start-up (`rules/efcore-rules.md`).

### The schema the DBA reviews

<!-- sample: tests/SkillSamples.Tests/AuthServer/AuthDbContext.cs -->
```csharp
public sealed class AppUser : IdentityUser<int>;
public sealed class AppRole : IdentityRole<int>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, AppRole, int>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity leaves some strings unbounded (nvarchar(max)): bound every one it left open. OpenIddict's
        // own JSON columns (payloads, permissions, redirect URIs) stay unbounded: a recorded exception.
        foreach (var entity in builder.Model.GetEntityTypes().Where(e => e.ClrType.Namespace?.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) == true
                                                                         || e.ClrType.Assembly == typeof(AppUser).Assembly))
        {
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null))
            {
                property.SetMaxLength(property.Name switch
                {
                    nameof(IdentityUser.PasswordHash) => 200,
                    nameof(IdentityUser.PhoneNumber) => 32,
                    nameof(IdentityUserClaim<int>.ClaimValue) or nameof(IdentityUserToken<int>.Value) => 1000,
                    _ => 256
                });
            }
        }
    }
}
```

Tested as a story (the DBA reviews the schema): the users have `int` keys, the tokens `bigint`; no binary
column; and the only `nvarchar(max)` columns left are OpenIddict's (`OpenIddictTokens.Payload` among
them). Those hold JSON and token payloads in a third-party schema: record them as the narrow exception
the column rules allow, as for Hangfire and Quartz. Identity's own unbounded columns (`PasswordHash`,
the stamps, claim values) are bounded above.

## Login, authorize, token, logout

<!-- sample: tests/SkillSamples.Tests/AuthServer/AuthServerEndpoints.cs -->
```csharp
public sealed record LoginRequest(string UserName, string Password);

public static class AuthServerEndpoints
{
    private const string OpenIddictScheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;

    public static IEndpointRouteBuilder MapAuthServer(this IEndpointRouteBuilder app)
    {
        // A real login page is a server-rendered form with antiforgery; the API shape keeps the sample short.
        app.MapPost("account/login", async (LoginRequest login, SignInManager<AppUser> signIn) =>
        {
            // lockoutOnFailure: true, or failed attempts are never counted and lockout never happens.
            var result = await signIn.PasswordSignInAsync(login.UserName, login.Password, isPersistent: false, lockoutOnFailure: true);
            // The same answer for a wrong password and a locked account: it tells an attacker nothing.
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });

        app.MapMethods("connect/authorize", [HttpMethods.Get, HttpMethods.Post], Authorize);
        app.MapPost("connect/token", Token);
        app.MapMethods("connect/logout", [HttpMethods.Get, HttpMethods.Post], Logout);
        return app;
    }

    private static async Task<IResult> Authorize(HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenIddict request.");
        var cookie = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = cookie.Succeeded ? await users.GetUserAsync(cookie.Principal!) : null;
        if (user is null || !await signIn.CanSignInAsync(user))
        {
            // Not signed in (or locked since): to the login page, then back here.
            var returnUrl = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
            return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [IdentityConstants.ApplicationScheme]);
        }

        var identity = await IdentityFor(user, users);
        identity.SetScopes(request.GetScopes());
        identity.SetResources(AuthServerSetup.OrdersApi);   // the services allowed to introspect it
        identity.SetDestinations(DestinationsOf);
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictScheme);
    }

    private static async Task<IResult> Token(HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenIddict request.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return Refuse(Errors.UnsupportedGrantType, "Only the code and refresh token flows are allowed.");

        // The principal stored with the code or the refresh token. Without this handler OpenIddict issues it
        // as it was at sign-in, so a role removed since then would stay for the refresh token's lifetime.
        var stored = (await http.AuthenticateAsync(OpenIddictScheme)).Principal!;
        var user = await users.FindByIdAsync(stored.GetClaim(Claims.Subject)!);
        if (user is null || !await signIn.CanSignInAsync(user))
            return Refuse(Errors.InvalidGrant, "The user can no longer sign in.");

        var identity = await IdentityFor(user, users);
        identity.SetScopes(stored.GetScopes());
        identity.SetResources(stored.GetResources());
        identity.SetDestinations(DestinationsOf);
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictScheme);
    }

    // Signs the user out and revokes every token issued to them, on every device. Ending only the
    // refresh token leaves the access token working until it expires.
    private static async Task<IResult> Logout(
        HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn, IOpenIddictTokenManager tokens)
    {
        var cookie = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (cookie.Succeeded && await users.GetUserAsync(cookie.Principal!) is { } user)
        {
            await tokens.RevokeBySubjectAsync(await users.GetUserIdAsync(user));
            await signIn.SignOutAsync();
        }

        return Results.SignOut(authenticationSchemes: [OpenIddictScheme]);
    }

    private static async Task<ClaimsIdentity> IdentityFor(AppUser user, UserManager<AppUser> users)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, await users.GetUserIdAsync(user))
            .SetClaim(Claims.Name, await users.GetUserNameAsync(user))
            .SetClaims(Claims.Role, [.. await users.GetRolesAsync(user)]);   // roles are rows, read every time
        return identity;
    }

    // Everything goes in the access token; name and roles also in the ID token when those scopes were granted.
    private static IEnumerable<string> DestinationsOf(Claim claim) => claim.Type switch
    {
        Claims.Name when claim.Subject!.HasScope(Scopes.Profile) => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
        Claims.Role when claim.Subject!.HasScope(Scopes.Roles) => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
        _ => [OpenIddictConstants.Destinations.AccessToken]
    };

    private static IResult Refuse(string error, string description) =>
        Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }), [OpenIddictScheme]);
}
```

Tested as stories, end to end against SQL Server:
- **An admin signs in through the portal:** login, the code flow with PKCE, a reference access token, and
  the orders API's introspection shows their name and `super-admin` role.
- **Not signed in:** the authorize request goes to the login page.
- **Someone intercepts the code but not the verifier:** the token request is refused (400).
- **Five wrong passwords:** the right one is refused too, and the account is locked. With
  `lockoutOnFailure: false`, which is what Identity's scaffolded login page passes, failures are never counted.
- **An admin loses their role:** the next refresh gives a token without it. That's what the token
  handler is for: OpenIddict's default reuses the principal stored with the refresh token.
- **Revoking only the refresh token leaves the access token working** until it expires. The old version
  of this skill said it didn't.
- **The admin logs out:** the access token is inactive at once and the refresh token is refused.
  `RevokeBySubjectAsync` ends every session of that user; to end one device only, revoke by the
  authorization id instead.

Tested as a story: `Admin@Example.COM` signs in as `admin@example.com`.

**On SQL Server, the image needs ICU.** Tested on a slim image's conditions: `Microsoft.Data.SqlClient`
refuses to open any connection in globalization-invariant mode (`NotSupportedException: Globalization
Invariant Mode is not supported`). An Alpine image without `icu-libs`, or one with
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, can't reach the database at all. Add ICU to the image
(`localization` covers it). PostgreSQL (Npgsql) works without it.

## Clients and roles, seeded as rows

<!-- sample: tests/SkillSamples.Tests/AuthServer/AuthServerSeed.cs -->
```csharp
// Run as a deploy step, like the reviewed migration script; safe to run again. Roles are rows: adding
// one later is an insert, not a code change. The admin's password comes from the secret store.
public static class AuthServerSeed
{
    public const string SuperAdmin = "super-admin";

    public static async Task SeedAsync(IServiceProvider services, string adminEmail, string adminPassword, Uri portalCallback, string ordersApiSecret)
    {
        await using var scope = services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (!await roles.RoleExistsAsync(SuperAdmin))
            Check(await roles.CreateAsync(new AppRole(SuperAdmin)));

        if (await users.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new AppUser { UserName = adminEmail, Email = adminEmail, LockoutEnabled = true };
            Check(await users.CreateAsync(admin, adminPassword));
            Check(await users.AddToRoleAsync(admin, SuperAdmin));
        }

        // The portal: a public client (a browser app can't keep a secret), so PKCE is required.
        if (await apps.FindByClientIdAsync("admin-portal") is null)
        {
            await apps.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "admin-portal",
                ClientType = ClientTypes.Public,
                RedirectUris = { portalCallback },
                PostLogoutRedirectUris = { new Uri(portalCallback, "/") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation, Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Profile, Permissions.Scopes.Roles, Permissions.Prefixes.Scope + "orders"
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange }
            });
        }

        // A service that checks tokens: a confidential client allowed only to introspect.
        if (await apps.FindByClientIdAsync(AuthServerSetup.OrdersApi) is null)
        {
            await apps.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = AuthServerSetup.OrdersApi,
                ClientSecret = ordersApiSecret,
                ClientType = ClientTypes.Confidential,
                Permissions = { Permissions.Endpoints.Introspection }
            });
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
```

- **Roles and permissions are data.** Code checks policies (`auth-patterns`); which roles grant them
  lives in tables. A new role is an insert.
- **An API can introspect only tokens issued for it:** the authorize handler sets the token's resources
  (`orders-api`), and OpenIddict refuses introspection from anyone else.

## Running several replicas

Every replica reads tokens from the same database, so revocation holds on all of them. The data
protection keys that protect the login cookie must be shared too (in the database or Redis), or a cookie
issued by one replica is rejected by the next.

## Rules
- Code flow with PKCE for browser apps; never implicit, never the password grant.
- Reference access tokens, introspected; the ID token only for the sign-in app.
- A token endpoint handler that reloads the user and their roles on every refresh.
- Logout revokes the tokens (`RevokeBySubjectAsync` or by authorization), not only the refresh token.
- `lockoutOnFailure: true`; one answer for a wrong password and a locked account.
- Identity's strings bounded; OpenIddict's JSON columns a recorded exception; `int` users, `bigint` tokens.
- Keys from the secret store with rotation; schema and seed applied as deploy steps.
