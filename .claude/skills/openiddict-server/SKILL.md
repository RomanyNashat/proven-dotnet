---
name: openiddict-server
description: Provider-side auth for .NET: build an OAuth2/OIDC Authorization Server with OpenIddict + ASP.NET Core Identity — opaque reference tokens, introspection/revocation/end-session, local accounts, RBAC. The issuing side (cf. auth-patterns).
version: 1.0.0
---

# OpenIddict Server — provider-side auth (we are the OpenID Provider)

For services that **issue and validate their own tokens** — the OpenID Provider / OAuth2 Authorization
Server role. This is the **opposite** of `auth-patterns`, which is the resource/consumer side (validating
tokens issued by an external IdP like Keycloak). Use this skill when you are the token authority
(your own sign-in server); use `auth-patterns` for services that merely validate.

Built on **OpenIddict** (Apache-2.0, OpenID-certified, no cloud dependency) + **ASP.NET Core Identity**
(the local user/credential store). OpenIddict owns the RFC plumbing and crypto; we own login, users,
claims, and RBAC. .NET 10, `sealed` types, primary constructors, records, `TypedResults`.
**Persistence: SQL Server** (EF Core) — see `sqlserver-patterns` and `efcore-patterns`.

> **Scope.** This covers **Phase 1**: build the server (issue, introspect, revoke, log out) with a full
> RBAC data model seeded to one super-admin row. It **seeds** the Phase-2 introspection-client pattern
> (§9) but does not build the shared NuGet package — that's a later phase.

## Token model (the core decision)
**Opaque reference access tokens + introspection**, not local JWT validation. Resource services never
read the token; they call `/introspect` and get identity + roles. Payoff: **immediate revocation**
(logout = row delete → next introspection fails) and one validation authority (no drift). Cost: a
per-request hop, bounded by admin-portal traffic and cut by short-TTL result caching on the resource
side (§9). The ID token is a JWT for the portal frontend only — never sent to resource services.

## 1. Server + Identity setup
```csharp
builder.Services
    .AddDbContext<AuthDbContext>(o =>
    {
        o.UseSqlServer(builder.Configuration.GetConnectionString("Auth"));
        o.UseOpenIddict();               // OpenIddict EF Core stores
    });

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(o =>
    {
        o.Password.RequiredLength = 12;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;   // local admin accounts, Phase 1
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();          // password reset, etc.
```
Identity provides hashing, lockout, and reset out of the box. `ApplicationUser`/`ApplicationRole` are
`sealed` classes extending `IdentityUser`/`IdentityRole` (GUID keys).

> **Migrations — reviewed scripts only.** The OpenIddict + Identity tables are created by EF Core
> migrations applied as **reviewed SQL scripts**, never by `Database.Migrate()`/`EnsureCreated()` in app
> startup (see `efcore-rules` — the migration-runner ban is non-negotiable, and it matters doubly for an
> auth service running multiple replicas).

## 2. Opaque reference tokens
```csharp
builder.Services.AddOpenIddict()
    .AddServer(o =>
    {
        o.UseReferenceAccessTokens();     // opaque — payload stays server-side
        o.UseReferenceRefreshTokens();    // revocation = delete the row
        o.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        o.SetRefreshTokenLifetime(TimeSpan.FromDays(14));
    });
```
**Why opaque:** the token is a random handle, not a self-describing JWT. Its claims live in the server
store, so (a) resource services must introspect to learn identity/roles, and (b) **revoking is a row
delete** — the next introspection returns `active: false` immediately. That's the whole point over
stateless JWT: instant logout/revocation.

## 3. Endpoints
```csharp
.AddServer(o =>
{
    o.SetAuthorizationEndpointUris("connect/authorize")
     .SetTokenEndpointUris("connect/token")
     .SetIntrospectionEndpointUris("connect/introspect")   // RFC 7662
     .SetRevocationEndpointUris("connect/revocation")      // RFC 7009
     .SetEndSessionEndpointUris("connect/logout")          // OIDC end-session
     .SetUserInfoEndpointUris("connect/userinfo");
    // discovery (/.well-known/openid-configuration) is automatic
    o.UseAspNetCore()
     .EnableAuthorizationEndpointPassthrough()
     .EnableTokenEndpointPassthrough()
     .EnableEndSessionEndpointPassthrough();
})
.AddValidation(o => { o.UseLocalServer(); o.UseAspNetCore(); });
```

## 4. Flows — authorization code + PKCE
Interactive portal login uses **authorization code + PKCE** (never implicit). Enable it, and host a
minimal server-rendered login page on the OP that signs the user in via Identity and issues the code.
```csharp
.AddServer(o =>
{
    o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
    o.AllowRefreshTokenFlow();
    // signing/encryption keys: dev vs prod — see §10
});
```
The `/connect/authorize` handler challenges to the login page when the user isn't authenticated, then
issues a `SignIn` with the resolved claims + roles once Identity validates credentials.

## 5. Application registration
Register clients as OpenIddict applications (seeded at startup via `IOpenIddictApplicationManager`):
- **Portal SPA** — `public` client (PKCE, no secret), redirect URIs, `authorization_code` + `refresh_token`
  grants, scopes `openid profile roles`.
- **Resource services** — `confidential` clients with **client credentials** and the
  **`ept:introspection`** permission, so only authenticated callers can hit `/introspect`. Each resource
  service authenticates itself when introspecting a portal user's token.

## 6. RBAC data model + seeding
Lay the **full** model down now even though only one role exists operationally:
`Users` / `Roles` / `Privileges` / `UserRoles` / `RolePrivileges`. Reuse Identity's user + role tables;
add `Privileges` and `RolePrivileges`. Seed **`super-admin` as a ROW** (plus one seeded super-admin
user) — **never a hardcoded role string** anywhere in code.
Adding future roles is inserting rows, not reshaping schema or editing code.
```csharp
// seeding (idempotent, run as a reviewed step — not EnsureCreated)
if (await roleManager.FindByNameAsync("super-admin") is null)
    await roleManager.CreateAsync(new ApplicationRole("super-admin"));
```

## 7. Stateless multi-replica
The auth service runs **multiple stateless replicas** — it's on the path of every portal request.
Token/session state and any validity cache live in **Redis as the single source of truth**, NOT in
per-replica memory (that makes validation inconsistent and lets logout silently fail on some replicas).
See `redis-patterns`. OpenIddict's stores are the DB; Redis
holds session/cache state. An optional per-replica in-memory cache is allowed **only** with a short TTL
or pub/sub invalidation.

## 8. Logout / revocation
Real logout = **invalidate the session + revoke the refresh token** (RFC 7009) — no new access tokens
can be minted, and the opaque access token fails its next introspection immediately (dead server-side).
Browser-side clearing alone does NOT revoke. Single portal app → RP-initiated logout via
`end_session_endpoint`; multiple apps under SSO → OIDC back-channel logout to propagate.
Build a **cache-purge path** (delete the token's cached entry on logout) **behind a feature flag** — the
resource-side introspection cache TTL is the only residual window; the flag lets you flip immediate
purge on operationally (feeds Phase 2).

## 9. Introspection-client pattern (seed of the Phase-2 package)

> **Tested version of the resource side:** `auth-patterns` §2 (OpenIddict.Validation with introspection
> against a real OpenIddict server: roles come back, a revoked token is refused on the next request, an
> unreachable server refuses the request). Use OpenIddict.Validation rather than hand-rolling the steps
> below. If the server issues JWT access tokens instead, it must call `DisableAccessTokenEncryption()`
> for services to validate them with JwtBearer (tested there).

How a **resource service** validates a portal token (this is the pattern the future shared NuGet package
will encapsulate — capture it here, don't build the package):
1. Extract the bearer token, POST it to `/connect/introspect`, authenticating with the resource
   service's own client id/secret (the `ept:introspection` client from §5).
2. Read `active` + identity + roles from the response.
3. **Cache the result in Redis, short TTL (~30–60s)** so validation isn't a network hop every request.
   TTL is the tuning knob between revocation latency and load.
4. **Fail closed** — if the auth service is unreachable, reject the request (use `polly-resilience` for
   timeout/circuit-breaker; a broken auth service must not become an open door).

## 10. Security posture
- **PKCE + state + nonce** on the code flow; HTTPS everywhere; timing-safe token handling (OpenIddict
  does this — don't hand-roll).
- **Signing/encryption keys:** dev may use `AddDevelopmentEncryptionCertificate()`/`SigningCertificate()`;
  **prod uses real certs from Key Vault** (see `secret-management`) with a **rotation** plan.
- Cross-cutting: `owasp-aspnetcore` (auth flows, CSRF), `healthcare-compliance` (this guards admin
  access to health data — audit the auth events, no PII in logs), `secret-management` (client secrets,
  keys), `zero-vulns` (no vulnerable dependencies).

## 11. Testing
Testcontainers integration test over the full slice (SQL Server + Redis containers):
**authorize → token → introspect (active) → revoke → introspect (inactive).** Assert the token is
`active: true` after issuance and `active: false` after revocation — that single flow proves issuance,
introspection, and revocation together. For downstream resource-service tests, reuse the `TestAuthHandler`
approach from `auth-patterns` to stub an authenticated portal user without standing up the OP.

## Rules
- This is the **issuing** side; `auth-patterns` is the **consuming** side — they're complements, not
  duplicates. Provider-side work → this skill; token-validation-only work → `auth-patterns`.
- Opaque reference tokens + introspection (not local JWT); ID token (JWT) is for the frontend only.
- Redis is the single source of truth for session/cache state; never per-replica memory.
- Fail closed on introspection; cache results short-TTL; purge-on-logout behind a flag.
- RBAC as data (rows), never hardcoded role strings; full model in Phase 1, super-admin seeded as a row.
- Migrations applied as reviewed scripts — never `Database.Migrate()`/`EnsureCreated()` in app code.
- Keys from Key Vault in prod, with rotation; PKCE/state/nonce; HTTPS.
