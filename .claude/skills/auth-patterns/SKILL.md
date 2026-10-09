---
name: auth-patterns
description: Resource/consumer-side auth for ASP.NET Core, for three token sources — Keycloak JWTs (roles mapped out of realm_access), JWTs from your own OpenIddict server, and its reference tokens through introspection — plus deny-by-default policies, 404 resource checks and client-credentials tokens. Tested in CI with real tokens and a real OpenIddict server. For the issuing side see openiddict-server.
version: 2.0.0
---

# Authentication & Authorization Patterns

**Scope: the resource side.** Validating tokens and authorizing with the identity they carry. The issuing
side, a server that mints and introspects tokens, is `openiddict-server`.

The code marked as a sample runs in CI (`tests/SkillSamples.Tests/Auth`): real RSA-signed JWTs through the
real JwtBearer pipeline, and a real OpenIddict server (Kestrel, store on SQL Server) for §2; every claim
marked *tested* is a test there.

## 0. Which token, which validation

A service can receive tokens from three places. Ask which before writing a line:

| Issuer | Token | The service | Revoked token |
|---|---|---|---|
| Keycloak | JWT | validates it itself (§1) | accepted until it expires |
| Your own OpenIddict server, JWT mode | JWT, **encryption off** | validates it itself, the same way (§2) | accepted until it expires (tested) |
| Your own OpenIddict server, reference mode | opaque reference token | asks `/connect/introspect` on every request (§2) | refused on the next request (tested) |

The server's configuration picks JWT or reference mode (`openiddict-server` §2); the service registers the
matching validation. Short access-token lifetimes (15–30 minutes, `rules/security.md`) bound how long a
revoked JWT keeps working; reference tokens trade a network call per request for revocation that's
immediate. `rules/security.md`
holds the non-negotiables (deny by default, 15–30 minute tokens, `ClockSkew` 30 s, ownership checks).

## 1. Validating Keycloak tokens

<!-- sample: tests/SkillSamples.Tests/Auth/KeycloakJwt.cs -->
```csharp
public static class KeycloakJwt
{
    public const string RoleClaim = "roles";

    public static AuthenticationBuilder AddKeycloakJwt(this IServiceCollection services, IConfiguration config) =>
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = config["Auth:Authority"];   // https://<keycloak>/realms/<realm>
                options.Audience = config["Auth:Audience"];     // Keycloak sends "account" unless the client has an audience mapper
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;               // keep "sub", "preferred_username" as they are
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = RoleClaim,
                };
                options.Events = new JwtBearerEvents { OnTokenValidated = context => AddKeycloakRoles(context, config["Auth:Audience"]) };
            });

    /// <summary>
    /// Keycloak puts roles inside JSON claims: "realm_access": { "roles": [...] } and
    /// "resource_access": { "&lt;client&gt;": { "roles": [...] } }. ASP.NET Core needs one claim per role, so
    /// RequireRole and IsInRole see nothing until they're copied out.
    /// </summary>
    private static Task AddKeycloakRoles(TokenValidatedContext context, string? client)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return Task.CompletedTask;
        }

        var roles = RolesIn(identity.FindFirst("realm_access")?.Value, "roles");
        if (client is not null)
        {
            roles = roles.Concat(RolesIn(identity.FindFirst("resource_access")?.Value, client, "roles"));
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            identity.AddClaim(new Claim(RoleClaim, role));
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<string> RolesIn(string? json, params string[] path)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var element = document.RootElement;
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return [];
            }
        }

        return element.ValueKind == JsonValueKind.Array
            ? [.. element.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!)]
            : [];
    }
}
```

```csharp
builder.Services.AddKeycloakJwt(builder.Configuration);
app.UseAuthentication();
app.UseAuthorization();
```

Tested:
- **Keycloak's roles are not claims ASP.NET Core can see.** They sit inside JSON claims (`realm_access`,
  `resource_access`). The old version of this skill set `RoleClaimType = "realm_access.roles"`, a claim
  that doesn't exist: an admin got 403 from `RequireRole("admin")`. With the mapping above, realm roles
  and the API client's roles both work, and a user without the role still gets 403.
- **Audience:** a token for another audience is 401. Keycloak puts `"aud": "account"` in access tokens
  unless the client has an audience mapper, the usual reason a valid-looking token fails here. Fix it in
  Keycloak, never by turning `ValidateAudience` off.
- **Lifetime:** a token expired 10 s ago is accepted (30 s skew); 90 s ago, 401.
- `Name` is `preferred_username`; `sub` stays `sub` (`MapInboundClaims = false`).
- Don't log `context.Exception.Message` from `OnAuthenticationFailed` at warning on every request: it's
  noise on expired tokens and can carry token details. The 401 is enough; APM counts them.

## 2. Tokens from your own OpenIddict server

<!-- sample: tests/SkillSamples.Tests/Auth/PortalTokens.cs -->
```csharp
/// <summary>
/// Tokens from your own OpenIddict server. It can issue either kind; the server's
/// configuration decides, and the resource service registers the matching one.
/// </summary>
public static class PortalTokens
{
    /// <summary>
    /// JWT access tokens, validated by each service like Keycloak's. The server must turn access-token
    /// encryption off (DisableAccessTokenEncryption): OpenIddict encrypts them by default, and JwtBearer
    /// rejects every encrypted token. OpenIddict's role claim is "role", a plain claim: no mapping needed.
    /// </summary>
    public static AuthenticationBuilder AddPortalJwt(this IServiceCollection services, IConfiguration config) =>
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = config["Auth:Authority"];
                options.Audience = config["Auth:Audience"];
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
            });

    /// <summary>
    /// Reference (opaque) access tokens: the service can't read them, so every request asks the server's
    /// introspection endpoint, authenticating as its own client. A revoked token fails on the next request.
    /// </summary>
    public static IServiceCollection AddPortalIntrospection(this IServiceCollection services, IConfiguration config)
    {
        services.AddOpenIddict().AddValidation(options =>
        {
            options.SetIssuer(new Uri(config["Auth:Authority"]!));
            options.UseIntrospection()
                .SetClientId(config["Auth:ClientId"]!)
                .SetClientSecret(config["Auth:ClientSecret"]!);
            options.UseSystemNetHttp();
            options.UseAspNetCore();
        });
        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        return services;
    }
}
```

Tested against a real OpenIddict server:
- **JWT mode needs `DisableAccessTokenEncryption()` on the server.** OpenIddict encrypts access tokens by
  default; JwtBearer can't read an encrypted token and every request is 401 (tested). With encryption off,
  the token is validated exactly like Keycloak's, and `RequireRole("admin")` works on OpenIddict's plain
  `role` claim, no mapping needed (tested).
- **A revoked JWT keeps working** until it expires: the service never asks the server (tested).
- **Reference mode:** the service introspects as its own client (`ept:introspection` permission, and the
  token's resources must name it). Roles come back in the introspection result; after a revoke, the next
  request is 401 (tested).
- **Server down, request refused:** with the introspection endpoint unreachable, the request doesn't get
  through (tested). Never catch that and let the request in.
- OpenIddict.Validation calls the server on every request; it doesn't cache. Caching the result (the
  `openiddict-server` §9 idea) makes revocation as slow as the cache time. Measure first: the
  traffic may not need it.
- OpenIddict's own client and HTTP packages bring Polly 7 with them. Reference only the packages you use
  (`OpenIddict.Validation.AspNetCore`, `OpenIddict.Validation.SystemNetHttp`), not the `OpenIddict.AspNetCore`
  metapackage, or Polly 7 and Polly 8 types collide in a service that uses `Microsoft.Extensions.Http.Resilience`
  (found building these samples).

## 3. Policies: deny by default

```csharp
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("AdminOnly", p => p.RequireRole("admin"));

app.MapGet("/health/live", () => "ok").AllowAnonymous();          // the exceptions are explicit
app.MapGet("/admin", ...).RequireAuthorization("AdminOnly");
```
Tested: no token is 401 everywhere except the `AllowAnonymous` endpoint. Policies name what the caller may
do (`CanManageOrders`), and map to roles in one place, so a role rename touches one line.

## 4. The record belongs to the caller: resource-based checks

<!-- sample: tests/SkillSamples.Tests/Auth/Visits.cs -->
```csharp
public sealed record VisitRecord(int Id, string PatientSub, string Summary);

/// <summary>The caller may see a visit if it's theirs, or if they're a doctor.</summary>
public sealed class VisitAccessRequirement : IAuthorizationRequirement;

public sealed class VisitAccessHandler : AuthorizationHandler<VisitAccessRequirement, VisitRecord>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, VisitAccessRequirement requirement, VisitRecord visit)
    {
        if (context.User.FindFirstValue("sub") == visit.PatientSub || context.User.IsInRole("doctor"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class VisitEndpoints
{
    // Someone else's visit is NotFound, like a visit that doesn't exist: a 403 would tell the caller the id
    // exists, and ids are sequential ints (rules/security.md).
    public static async Task<Results<Ok<VisitRecord>, NotFound>> GetVisit(
        int id, ClaimsPrincipal user, IAuthorizationService authorization, IVisitStore visits, CancellationToken ct)
    {
        var visit = await visits.FindAsync(id, ct);
        if (visit is null || !(await authorization.AuthorizeAsync(user, visit, new VisitAccessRequirement())).Succeeded)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(visit);
    }
}

public interface IVisitStore
{
    Task<VisitRecord?> FindAsync(int id, CancellationToken ct);
}
```

```csharp
builder.Services.AddSingleton<IAuthorizationHandler, VisitAccessHandler>();
app.MapGet("/visits/{id:int}", VisitEndpoints.GetVisit);
```
Tested: the patient sees their visit, a doctor sees it, another patient gets 404, the same as a visit that
doesn't exist. Every endpoint that takes an id needs this, or the query filtered by owner
(`rules/security.md`); a list query filters by owner in SQL instead of loading and checking each row.

## 5. The current user

Read the caller from `ClaimsPrincipal` (an endpoint parameter) or a small scoped service over
`IHttpContextAccessor` (`ICurrentUserService`, the name the other skills use):
- `sub` is the IdP's id for the user: an opaque string. Keep it as a string (or map it to your own `int`
  user id in a table); don't `Guid.Parse` it, a parse error becomes a 500.
- Never read the user in a singleton, a pooled `DbContext`'s options or an interceptor registered in them:
  those are built once, and every request gets the first caller (`efcore-patterns` §1, §4).
- Tenant or clinic scoping that the database must enforce too: row-level security (`sqlserver-patterns`
  §9) or a query filter that reads a context member (`efcore-patterns` §3).

## 6. Service-to-service: client credentials

<!-- sample: tests/SkillSamples.Tests/Auth/ServiceTokenHandler.cs -->
```csharp
public sealed record ServiceToken(string AccessToken, TimeSpan ExpiresIn);

/// <summary>Client credentials against the IdP's token endpoint.</summary>
public interface IServiceTokenClient
{
    Task<ServiceToken> RequestAsync(string audience, CancellationToken ct);
}

/// <summary>
/// Adds a client-credentials token to every outgoing call, cached until shortly before it expires (from the
/// token response, not a fixed guess). Attach it with the factory overload, one per audience:
/// <c>.AddHttpMessageHandler(sp => new ServiceTokenHandler(sp.GetRequiredService&lt;IServiceTokenClient&gt;(), sp.GetRequiredService&lt;IMemoryCache&gt;(), "inventory"))</c>.
/// </summary>
public sealed class ServiceTokenHandler(IServiceTokenClient tokens, IMemoryCache cache, string audience) : DelegatingHandler
{
    private static readonly TimeSpan RefreshEarly = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await cache.GetOrCreateAsync($"service-token:{audience}", async entry =>
        {
            var issued = await tokens.RequestAsync(audience, cancellationToken);
            entry.AbsoluteExpirationRelativeToNow = issued.ExpiresIn > RefreshEarly * 2 ? issued.ExpiresIn - RefreshEarly : issued.ExpiresIn / 2;
            return issued.AccessToken;
        });

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
```

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("inventory", c => c.BaseAddress = new Uri(config["Services:Inventory:Url"]!))
    .AddHttpMessageHandler(sp => new ServiceTokenHandler(
        sp.GetRequiredService<IServiceTokenClient>(), sp.GetRequiredService<IMemoryCache>(), "inventory"))
    .AddStandardResilienceHandler();
```
Tested: two calls share one token, and the token is renewed once its cache time (from `expires_in`) runs
out. `AddHttpMessageHandler<ServiceTokenHandler>()` with no DI registration fails the moment the client
is created (tested), which is how the old version of this skill was wired.

## 7. Tests

Integration tests replace the bearer scheme with a test handler that reads the caller from a header and
**returns no user when the header is missing**, so 401 paths can be tested too: `testing-integration` §2
(tested). A test handler that always signs someone in hides every missing `[Authorize]`.

## 8. Review checklist
- `RoleClaimType = "realm_access.roles"` or `"roles"` with no mapping from Keycloak's JSON claims.
- JwtBearer against your own OpenIddict server while the server still encrypts access tokens; a resource service
  that catches an introspection failure and lets the request through.
- `ValidateAudience = false`, `ValidateLifetime = false`, `ClockSkew` left at 5 minutes,
  `RequireHttpsMetadata = false` outside local development.
- No fallback policy; an endpoint open without `AllowAnonymous` written next to it.
- An id lookup with no ownership check, or one that answers 403 for someone else's record.
- `Guid.Parse` on `sub`; the current user read in a singleton or in pooled options.
- A service token cached for a fixed time instead of its `expires_in`; `AddHttpMessageHandler<T>()` without
  registering `T`.
- A test auth handler that authenticates requests with no test header.
