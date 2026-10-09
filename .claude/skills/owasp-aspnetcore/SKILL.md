---
name: owasp-aspnetcore
description: OWASP Top 10:2025 mapped to ASP.NET Core services — which skill holds the tested fix for each category, plus the ones this skill owns — security headers that survive the error path, HSTS, CORS, an SSRF guard that checks the resolved address, and JSON polymorphism by allowlist. Tested in CI.
version: 2.0.0
---

# OWASP Top 10:2025 for ASP.NET Core

The 2025 list renumbered the 2021 one. SSRF is no longer its own category (it is part of A01), supply
chain has its own (A03), and A10 is new: mishandling exceptional conditions. Use the 2025 numbers in
reviews.

Most fixes already live in the skill that owns the area, tested there. This skill maps each category to
that place and owns the rest: headers, HSTS, CORS, SSRF and deserialization. Code marked as a sample runs
in CI (`tests/SkillSamples.Tests/Owasp`); every claim marked *tested* is a test there.

## 0. The list, and where each fix is

| 2025 | In a .NET service | The tested fix |
|---|---|---|
| **A01** Broken Access Control | deny by default; someone else's record is a 404; CORS; SSRF | `auth-patterns` §3–4; §3 and §4 here |
| **A02** Security Misconfiguration | headers, HSTS, no internals in errors, dev-only features off | §1–2 here; `api-design` §2 |
| **A03** Software Supply Chain Failures | known-vulnerable packages, where packages come from, pinned CI | `zero-vulns`; §6 here |
| **A04** Cryptographic Failures | AES-GCM at rest, keys from the vault, TLS | `encryption-patterns`, `secret-management`, `nginx` |
| **A05** Injection | parameters, never concatenation; validation before the handler | `efcore-patterns`, `dapper-patterns`, `cqrs-eventsourcing` (validation decorator) |
| **A06** Insecure Design | per-caller rate limits, size limits, business rules in the domain | `api-design` §6, `nginx` (body size), `ddd-patterns` |
| **A07** Authentication Failures | token validation for the three token sources, service tokens | `auth-patterns` §0–2, §6 |
| **A08** Software or Data Integrity Failures | no type names from the wire | §5 here |
| **A09** Security Logging and Alerting Failures | log security events, never patient data or tokens | `observability`, `pii-masking` |
| **A10** Mishandling of Exceptional Conditions | fail closed; an error says nothing internal | `api-design` §2, `auth-patterns` §2; §7 here |

`rules/security.md` holds the non-negotiables. This skill doesn't repeat them.

## 1. Security headers on every response (A02)

Headers added in `app.Use` before `next()` are missing from the 500s: the exception handler clears every
response header before it writes the ProblemDetails (tested, even with the middleware first). Add them in
`OnStarting`, which runs for every response, errors and 404s included (tested).

<!-- sample: tests/SkillSamples.Tests/Owasp/SecurityHeaders.cs -->
```csharp
public static class SecurityHeaders
{
    // Headers for a JSON API. They're added in OnStarting, not before next(): the exception handler clears
    // every response header before it writes its ProblemDetails, so headers added on the way in are missing
    // from exactly the error responses. Register this first in the pipeline.
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                var headers = http.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";   // an API serves no pages
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                if (!headers.ContainsKey("Cache-Control"))
                {
                    headers["Cache-Control"] = "no-store";   // patient data must not sit in a shared cache
                }

                return Task.CompletedTask;
            });
            return next(http);
        });
}
```

- An endpoint that sets its own `Cache-Control` (reference data, say) keeps it (tested).
- A service that serves HTML needs its own CSP; `default-src 'none'` is for APIs.
- Also off in production: the OpenAPI document and UI (map them only in Development, or behind auth), and
  Kestrel's `Server` header (`AddServerHeader = false`) if Kestrel faces clients directly.

## 2. HSTS (A02)

`UseHsts` sends the header only over HTTPS, and never to `localhost` (tested), so it won't show up in local
runs. Its default `max-age` is 30 days (tested); set a year.

```csharp
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });
app.UseHsts();
```

Behind nginx the request arrives as `http`, so `UseHsts` sends nothing until `ForwardedHeaders` passes the
original scheme through (`nginx` §3). Or send the header from nginx; agree with SRE which layer owns it.

## 3. CORS (A01)

Only the portal's origins, from configuration. A bearer-token API doesn't need `AllowCredentials`: the
token goes in a header, and credentials are for cookies.

<!-- sample: tests/SkillSamples.Tests/Owasp/PortalCors.cs -->
```csharp
public static class PortalCors
{
    public const string Policy = "portal";

    // Only the portal's own origins, from configuration. No AllowCredentials: the portal sends the token in
    // the Authorization header, and credentials are only for cookies. Never SetIsOriginAllowed(_ => true)
    // with AllowCredentials: it echoes back any origin and lets any site call the API as the signed-in user.
    public static IServiceCollection AddPortalCors(this IServiceCollection services, string[] origins) =>
        services.AddCors(o => o.AddPolicy(Policy, policy => policy
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type")));
}
```

- Another origin gets no `Access-Control-Allow-Origin` at all (tested).
- `AllowAnyOrigin().AllowCredentials()` throws at startup (tested), which is why people reach for
  `SetIsOriginAllowed(_ => true)`. With credentials, that answers `https://evil.example` with its own origin
  and `Allow-Credentials: true` (tested). It is the same hole with the check removed.

## 4. SSRF: calling a URL someone else chose (A01)

A partner's webhook URL, an image URL in a request, a callback in a FHIR message. Checking the URL's host
name doesn't work: the name can resolve to `10.x`, `127.0.0.1` or the cloud metadata address
`169.254.169.254`, and can resolve differently a second later (DNS rebinding). Check the address the socket
is about to connect to, on every connection:

<!-- sample: tests/SkillSamples.Tests/Owasp/OutboundGuard.cs -->
```csharp
public sealed class BlockedDestinationException(string host, IPAddress address)
    : IOException($"Outbound call to {host} refused: it resolves to {address}, which is not a public address.");

/// <summary>
/// For calls to a URL someone else chose (a partner's webhook, an image URL in a request). The check runs on
/// the address the socket is about to connect to, after DNS, on every connection including redirects. A check
/// on the URL's host name can't do that: the name can resolve to 10.x, 127.0.0.1 or the cloud metadata
/// address, and can resolve differently a second later (DNS rebinding).
/// </summary>
public static class OutboundGuard
{
    private static readonly IPNetwork[] NotPublic =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),     // carrier-grade NAT, used inside some clusters
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),    // link-local, including the cloud metadata service 169.254.169.254
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("fc00::/7"),          // unique local
        IPNetwork.Parse("fe80::/10"),         // link-local
        IPNetwork.Parse("ff00::/8"),
    ];

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();   // ::ffff:10.0.0.1 is 10.0.0.1
        }

        return !NotPublic.Any(network => network.Contains(address));
    }

    public static IHttpClientBuilder AddPublicOnlyHttpClient(this IServiceCollection services, string name) =>
        services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => CreateHandler());

    public static SocketsHttpHandler CreateHandler() => new()
    {
        // Through a proxy, the socket connects to the proxy, so this check would only ever see the proxy's
        // address. A client that must use a proxy needs the proxy to block internal addresses instead.
        UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            if (addresses.Length == 0)
            {
                throw new HttpRequestException($"{host} did not resolve.");
            }

            // Refuse if ANY address is internal: a name with one public and one private record is an attack.
            if (addresses.FirstOrDefault(a => !IsPublic(a)) is { } blocked)
            {
                throw new BlockedDestinationException(host, blocked);
            }

            // Connect to the addresses just checked, not to the name: resolving again could give another answer.
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
```

- Tested against a real service on `127.0.0.1`: a plain client reaches it, the guarded client is refused,
  by IP and by the name `localhost`. The address list is tested case by case, the IPv4-mapped form included.
- `IPAddress` has no `IsPrivate()` (tested); `IPNetwork.Contains` is the check.
- Use this only for URLs from outside. Calls to your own services and to fixed partners use a named client
  with a configured base address, and never take a full URL from a request.
- If partners are known, an allowlist of their hosts comes first. The guard is still needed: an
  allowlisted name can still be pointed at an internal address.

## 5. Deserialization (A08)

`System.Text.Json` only. Polymorphic bodies pick from a closed list; the type never comes from the wire.

<!-- sample: tests/SkillSamples.Tests/Owasp/PaymentMethod.cs -->
```csharp
// Polymorphic JSON with System.Text.Json: the discriminator picks from this list and nothing else. An unknown
// "kind" is a 400, never an instance of some other type. (Newtonsoft's TypeNameHandling other than None reads
// a .NET type name from the request and creates it: a remote code execution route.)
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CardPayment), "card")]
[JsonDerivedType(typeof(InsurancePayment), "insurance")]
public abstract record PaymentMethod;

public sealed record CardPayment(string Last4) : PaymentMethod;

public sealed record InsurancePayment(string PolicyNumber) : PaymentMethod;
```

- `"kind": "card"` binds a `CardPayment`; a .NET type name as the kind is a 400 (tested).
- `AllowOutOfOrderMetadataProperties = false` is the default (tested), not a security setting. Leave it.
- `BinaryFormatter` is gone from .NET 9 and banned in `rules/security.md`. Newtonsoft stays on
  `TypeNameHandling.None`.
- Request DTOs carry only the fields a caller may set. An entity bound from a body lets a caller set
  `IsVerified` or `OwnerId` (mass assignment).

## 6. Supply chain (A03)

- Zero known-vulnerable packages, transitive included, as a build error: `zero-vulns`.
- Internal packages come only from your internal feed: `<packageSourceMapping>` in `nuget.config`, so
  a public package with the same name can't be pulled in its place.
- CI images pinned by digest and included templates by a fixed ref, not a tag that can move.

## 7. Exceptional conditions: fail closed (A10)

- An error that stops a check refuses the request. When the introspection server is down, the request is
  refused (`auth-patterns` §2, tested). An authorization handler that throws is a 500, never a pass.
- An unexpected exception is a ProblemDetails with a trace id and nothing internal (`api-design` §2,
  tested), and the security headers are still on it (§1).
- `catch (Exception) { }` is banned (`rules/csharp-standards.md`). A catch that logs and carries on in a
  security check is the same bug.

## 8. Review checklist (used by `security-reviewer`)

- [ ] Fallback policy requires an authenticated user; someone else's record is a 404 (`auth-patterns`)
- [ ] Security headers set in `OnStarting`, first in the pipeline; present on 500s
- [ ] HSTS: one year, and actually sent behind nginx (ForwardedHeaders, or nginx sends it)
- [ ] CORS: listed origins only; no `AllowCredentials` for bearer APIs; never `SetIsOriginAllowed(_ => true)`
- [ ] Any URL from outside goes through the outbound guard; nothing else takes a full URL from a request
- [ ] No type names from the wire: STJ polymorphism by allowlist, Newtonsoft `TypeNameHandling.None`
- [ ] Request DTOs, never entities, bound from bodies
- [ ] Per-caller rate limits on login, OTP and expensive endpoints (`api-design` §6)
- [ ] Zero vulnerable packages; package source mapping for internal packages (`zero-vulns`)
- [ ] Security events logged without tokens or patient data (`observability`, `pii-masking`)
- [ ] Every failure path refuses: no `catch` that lets a request through
