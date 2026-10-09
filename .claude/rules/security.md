# Security Standards

## Security by Default
- Deny-by-default authorization: `[Authorize]` globally, `[AllowAnonymous]` explicitly on public endpoints
- All endpoints require authentication unless explicitly opted out
- HTTPS everywhere in production — enforce via HSTS middleware
- Security headers: X-Content-Type-Options, X-Frame-Options, Content-Security-Policy, Strict-Transport-Security

## Input Validation
- FluentValidation or DataAnnotations on EVERY command/request DTO — no unvalidated input reaches business logic
- Parameterized queries ALWAYS — never concatenate user input into SQL strings
- Use `FormattableString` with EF Core's `FromSql()` — compiler-enforced parameterization
- Validate file uploads: size limits, content-type allowlist, no path traversal
- Use `HtmlEncoder.Default.Encode()` for any user content rendered in HTML

## Authentication & Authorization
- JWT Bearer tokens with short expiry (15-30 minutes), refresh tokens for renewal
- `ClockSkew = TimeSpan.FromSeconds(30)` — not the default 5 minutes
- `RequireHttpsMetadata = true` in production (even behind load balancer if TLS terminated there)
- Policy-based authorization over role-based: `[Authorize(Policy = "CanManageOrders")]`
- Never store passwords — use ASP.NET Core Identity with Argon2id, or PBKDF2 at OWASP's current counts
  (HMAC-SHA256 600k, HMAC-SHA512 220k), never SHA1. Identity's default is lower: set
  `PasswordHasherOptions.IterationCount` (existing hashes are upgraded at each user's next login)

## Secrets
- NEVER commit secrets, connection strings, API keys, or certificates to source control
- Use `dotnet user-secrets` for local development
- Use Azure Key Vault or HashiCorp Vault for production
- Environment variables for container deployments (injected via K8s Secrets or Vault Agent)
- Rotate secrets on a schedule. Support zero-downtime rotation in code.

## Cryptography
- AES-GCM for symmetric encryption (12-byte nonce, 16-byte auth tag, 256-bit key)
- Never reuse nonces — generate cryptographically random nonce per encryption operation
- PBKDF2-HMAC-SHA256 with 600,000 iterations for key derivation (or Argon2id if available)
- TLS 1.3 for all service-to-service communication
- No MD5 or SHA1 for anything security-sensitive

## Data Protection
- PII must be encrypted at rest and masked in logs
- Serilog sensitive data masking: never log passwords, tokens, SSNs, credit cards, health records
- API responses must never expose stack traces or database errors in production
- **Every lookup by ID checks ownership.** Sequential `int` keys appear in URLs, so anyone can guess
  the next one (and GUIDs leak through logs, links and referrers). Every endpoint that takes an ID must confirm the caller may see *that*
  record (resource-based authorization: the query filters by owner/tenant, or an authorization handler
  checks it). A missing check is a data leak (IDOR / broken object-level authorization).
- Use `ProblemDetails` (RFC 7807) for error responses — structured, no information leakage

## Dependency Vulnerabilities (zero known vulns — enforced)
- **Zero known-vulnerable NuGet packages** ship — direct AND transitive, any severity. A known vuln
  is a **hard fail** that blocks merge, not a note in a report. See `skills/zero-vulns/`.
- `Directory.Build.props` enables `NuGetAudit` + `NuGetAuditMode=all` and treats `NU1901–NU1904` as
  **errors**, so the build fails on any vulnerable package (direct or transitive).
- Transitive vuln → pin the fixed version with a direct `PackageReference` (documented with the
  advisory link). No silent suppression — an un-fixable vuln is a tracked, owned decision.

## Banned
- `BinaryFormatter` — insecure deserialization (remote code execution); removed from .NET 9
- `System.Web` namespace in ASP.NET Core applications
- Direct `HttpClient` instantiation (socket exhaustion, DNS caching issues)
- `[ValidateAntiForgeryToken]` bypass — always validate on state-changing operations
- Disabling SSL validation in HttpClient handlers (even "temporarily")
