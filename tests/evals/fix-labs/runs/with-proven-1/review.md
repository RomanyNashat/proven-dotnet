# LabResults: consolidated review

Sources: **C** code review, **S** security review, **D** database review, **K** known-traps scan.
Paths are under `LabResults/`. Each finding gives who raised it, where, the fix and the skill it cites.
"Settled" lines resolve disagreements between reviewers. Every fix keeps what `task.md` says must stay:
`Program`, the four routes and their shapes, `LabDbContext`, the typed `LabClient`/`SmsClient`,
`TimeProvider` from DI, JWT bearer on the default scheme (`Auth:Authority`, audience `lab-results`), and
the three settings.

**Verdict: not ready for production.** Clinicians get 403 on every route, `recordedBy` is wrong on every
result, one SMS timeout stops the whole service, and national IDs reach the logs.

**Fix order:** 1 → 2 → 3. Scope validation (3) makes the current code throw, so it goes in with or after
the fix for 2 (S, D).

---

## Critical

### 1. Keycloak roles never reach `RequireRole`, so every clinician gets 403 — C, S, D, K
- **Where:** `Program.cs:32` (`RoleClaimType = "realm_access.roles"`), `Program.cs:37`
- **Why:** Keycloak sends `realm_access` as one JSON claim, so no claim matches `realm_access.roles`. It
  fails closed, but all three clinician routes are unusable and the hidden tests depend on them.
- **Fix:** `MapInboundClaims = false`, `RoleClaimType = "roles"`, and in `OnTokenValidated` copy each
  role out of `realm_access.roles` (and `resource_access.lab-results.roles` if the client uses client
  roles) as a `roles` claim. Stay on the default JwtBearer scheme.
- **Skill:** `auth-patterns` §Keycloak, §1, §8
- **Settled:** K rated this a warning; it is Critical because it blocks every clinician route. C's
  `IClaimsTransformation` and S's `OnTokenValidated` do the same job, so use `OnTokenValidated`: the
  mapping stays with the JWT configuration, and `MapInboundClaims = false` also keeps `sub` as `sub`,
  which finding 2 relies on.

### 2. `recordedBy` is stamped with the first caller's `sub` (or `"system"`) on every result — C, S, D
- **Where:** `Program.cs:13-17` (`AddDbContextPool`), `Infrastructure/AuditInterceptor.cs:8`,
  `Infrastructure/CurrentUser.cs:8-10`
- **Why:** the pool builds its options once, from the root provider, so the scoped `AuditInterceptor`
  and its `CurrentUser` are created once and kept for good. `CurrentUser` reads `sub` in its
  constructor. The clinical audit trail is wrong with no error, and the hidden tests check
  `recordedBy == sub`.
- **Fix:** in `RecordAsync`, take the `ClaimsPrincipal`, read `sub` with `FindFirstValue("sub")`, return
  401 if it's missing (never fall back to `"system"`), and pass it into `LabResult.Record(..., recordedBy)`.
  Remove the interceptor's user stamping.
- **Skill:** `efcore-patterns` §1, §4 (lines 211-214, checklist line 405); `auth-patterns` l.285
- **Settled:** there were three fixes on the table: a singleton interceptor reading `IHttpContextAccessor`
  (C, S), `AddDbContext` (C), or a pooled factory with a `CallerId` set on each rental (D). Pass `sub`
  explicitly (S). There is one write path, so the value is visible where it's set, it can be unit-tested
  without an `HttpContext`, and the pool can stay.

### 3. One SMS timeout stops the whole API — C, S, D
- **Where:** `NotificationQueue.cs:30`
- **Why:** only `HttpRequestException` is caught. An `HttpClient` timeout throws `TaskCanceledException`,
  which escapes `ExecuteAsync`, and the default `BackgroundServiceExceptionBehavior.StopHost` stops the
  host.
- **Fix:** `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)`, log `ex.GetType().Name`
  and the status code rather than the exception text (S, also covers `NotificationQueue.cs:32`), and give
  `SmsClient` a short timeout (see 12).
- **Skill:** `worker-patterns` §polling worker
- **Settled:** S rated this High; it is Critical because one slow gateway response takes down every
  endpoint.

### 4. Full national IDs logged by `HttpClient` on every lab lookup — C, S
- **Where:** `LabClient.cs:11`, `Program.cs:21`, `appsettings.json:15-17`
- **Why:** the lab API takes the national ID in the URL **path**. `IHttpClientFactory` logs the request
  URI at Information, and .NET redacts query strings, not paths. `appsettings.json` only quiets
  `Microsoft.AspNetCore`.
- **Fix:** `.RemoveAllLoggers()` on the `LabClient` builder, plus your own log line with the status code
  only.
- **Skill:** `pii-masking` (lines 105-110)
- **Settled:** S rated this High; it is Critical because it leaks every lookup in production, with no
  unusual input needed. Use `RemoveAllLoggers()` on the one client rather than C's
  `"System.Net.Http.HttpClient": "Warning"`, which would also hide the other client's logs. A custom
  `IHttpClientLogger` (C) is only worth it if request logging is wanted back.

---

## High

### 5. The national-ID mask fails open — C, S
- **Where:** `Masking.cs:9-11`, `ResultEndpoints.cs:41`
- **Why:** `[0-9]{10}` misses Arabic-Indic digits (`١٠١٢…`), spaced input and 9- or 11-digit values, and
  logs them whole. `NationalId` is never validated.
- **Fix:** log `PatientId` instead of the national ID. Where an ID must be logged, mask by refusal:
  `"[redacted]"` unless the value is exactly 10 ASCII digits. Normalise and validate the input as in 6.
- **Skill:** `pii-masking` (line 29, `SaudiNationalIdMaskingOperator` at line 166)
- **Settled:** C rated this Critical; it is High because it needs unusual input, whereas 4 leaks on every
  call. Use S's mask-by-refusal rather than C's "keep the last 4 of anything": an unknown format is never
  printed.

### 6. Input is barely validated (path traversal to the lab API, SMS pumping, 500s from the DB) — C, S, D
- **Where:** `ResultEndpoints.cs:33-36`, `ResultEndpoints.cs:63`, `LabClient.cs:11`
- **Why:**
  - `NationalId` is unchecked. `Uri.EscapeDataString("..")` leaves `..` as it is, so
    `patients/../orders` reaches `{base}/orders` with the service's identity. A null value throws a 500.
  - `Phone` is unchecked, so a clinician can send SMS to any number. It is also never checked against
    the patient.
  - `PatientId > 0` isn't checked.
  - `Value` isn't range-checked: `numeric(10,3)` holds at most 9,999,999.999, so more raises `22003` (500).
  - `Notes` has no length check, so `22001` (500) once 7 is fixed.
  - Unit errors are reported under the `"Test"` key.
- **Fix:** a FluentValidation validator on both request DTOs: normalise digits, then `^[0-9]{10}$` for
  `NationalId` (400 otherwise); `^\+9665[0-9]{8}$` for `Phone`; `PatientId > 0`; `Value` within
  `numeric(10,3)`; `Notes` ≤ 1000 (see 7); each error under its own key.
- **Skill:** security rules (validation on every DTO); `postgresql-patterns` for the error codes
- **Settled:** C rated the traversal Critical on its own; it is High, because the caller is already an
  authenticated clinician, and one validator fixes it together with the rest.

### 7. `Notes` is an unbounded `text` column — C, S, D
- **Where:** `LabDbContext.cs:12-22`
- **Why:** no `HasMaxLength`, which breaks the house column rule, and it is free text that can hold
  patient data.
- **Fix:** `e.Property(r => r.Notes).HasMaxLength(1000)` and the same limit in the validator (6).
- **Skill:** `efcore-rules`; `postgresql-patterns` §1
- **Settled:** severity ranged from Critical (C) to Medium (S); it is High: a hard rule, but no data at
  risk yet, and the tests build the schema from the model. Limit: 1000 (D) over 2000 (S): the rule says
  size it smaller when in doubt, and raising it later is a cheap reviewed `ALTER`. Confirm with the
  clinicians.

### 8. Scope validation is off outside Development, which hid finding 2 — C, S, D, K
- **Where:** `Program.cs:9`
- **Fix:** `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })`
  in every environment, after or with the fix for 2, or the service fails at start-up.
- **Skill:** `dotnet-core` (Rules)
- **Settled:** K rated it info and C rated it Critical; it is High. On its own it causes no harm, but it
  is what let 2 reach Production silently.

### 9. JWT `ClockSkew` left at 5 minutes; `RequireHttpsMetadata` not set — C, S
- **Where:** `Program.cs:29-33`
- **Fix:** `ClockSkew = TimeSpan.FromSeconds(30)`, `RequireHttpsMetadata = true`.
- **Skill:** `auth-patterns` §8; security rules

---

## Medium

### 10. Paging skips rows with equal timestamps; a bad cursor gives a 500 — C, S, D
- **Where:** `ResultEndpoints.cs:54-59`, index at `LabDbContext.cs:20`
- **Why:**
  - Cursor and order use `ReceivedAt` only, so `<` drops any row that shares the boundary timestamp
    (common with a fixed `TimeProvider`).
  - `DateTimeOffset.Parse` throws on bad input.
  - A cursor with a non-zero offset is refused by Npgsql for `timestamptz`.
- **Fix:**
  - Order by `ReceivedAt DESC, Id DESC`, and encode both in the opaque `next` string.
  - Filter with `r.ReceivedAt < c || (r.ReceivedAt == c && r.Id < id)`.
  - `TryParse` the cursor, return 400 on failure, then `.ToUniversalTime()`.
  - Replace the index with `HasIndex(r => new { r.ReceivedAt, r.Id })`.
  - Keeps the contract: `{ items, next }`, newest first, `next` passed back as `before`.
- **Skill:** `efcore-patterns` §9; `api-design` (keyset paging); `postgresql-patterns` §2

### 11. No error handling or security headers — C, S
- **Where:** `Program.cs:39-44`
- **Fix:** `AddProblemDetails()`, `UseExceptionHandler()`, `UseStatusCodePages()`. Add HSTS, `nosniff`
  and `X-Frame-Options`, set in `OnStarting` so they also appear on 500s.
- **Skill:** `owasp-aspnetcore`; architecture rules

### 12. Outbound HTTP has no timeout or resilience; a lab 404 becomes a 500 — C, S
- **Where:** `Program.cs:21-22`
- **Fix:** `AddStandardResilienceHandler()` on both typed clients with a short timeout; no retries on the
  SMS POST unless the gateway takes an idempotency key. Map a lab 404 (unknown patient) to an empty list.
- **Skill:** `polly-resilience`

### 13. SMS notifications are lost on restart, and `POST /results` can block or double-save — C, S, D
- **Where:** `NotificationQueue.cs:10`, `ResultEndpoints.cs:40-43`
- **Why:** the channel is in memory. When the gateway is slow the bounded `WriteAsync` waits. If the
  request is cancelled there, the result is already saved, the client retries, and the result is stored
  twice.
- **Fix:** a transactional outbox table written in the same `SaveChanges` as the result, drained by the
  worker through `SmsClient`.
- **Skill:** `outbox`
- **Settled:** C offered "`TryWrite` and log a drop" as a fallback. Use the outbox: `TryWrite` stops the
  blocking but still loses messages, and the service is going to production.

### 14. The SMS names the test — C, S
- **Where:** `ResultEndpoints.cs:43`
- **Why:** health data (for example HIV or HbA1c) goes to the gateway and shows on lock screens.
- **Fix:** a generic "your result is ready" message, from resources rather than hard-coded Arabic (C).
- **Skill:** `healthcare-compliance`; `localization` for the resource text

### 15. No rate limit on the national-ID lookup — S
- **Where:** `ResultEndpoints.cs:25`
- **Why:** one token can enumerate national IDs through the lab API.
- **Fix:** a per-`sub` rate limiter on `POST /lab-orders/lookup`.
- **Skill:** `api-design` (per-caller rate limits)

### 16. Connection pool unsized; credentials in `appsettings.json` — D
- **Where:** `appsettings.json:3`
- **Why:** Npgsql defaults to 100 per pod, so pods × 100 exceeds PostgreSQL's default `max_connections`
  of 100. D notes that the password must not live in this file.
- **Fix:** e.g. `Maximum Pool Size=20;Timeout=15;Command Timeout=30`, agreed with the DBA. Inject
  `ConnectionStrings:Lab` from a secret store or the environment (the setting name stays). If a real
  password is committed, rotate it.
- **Skill:** `postgresql-patterns` §3; `secret-management`

### 17. Package audit not enforced or run — C, S
- **Where:** `LabResults.csproj`
- **Why:** there is no `NuGetAudit` / `NuGetAuditMode=all` with NU1901–NU1904 as errors.
  `dotnet list package --vulnerable --include-transitive` wasn't run (JwtBearer 10.0.0 and
  Npgsql.EF 10.0.0 are unaudited). Any hit blocks merge.
- **Fix:** add the audit properties (in `Directory.Build.props`) and run the audit before merge.
- **Skill:** `zero-vulns`

---

## Low

### 18. The primary key is `BY DEFAULT` rather than `ALWAYS` identity — D
- **Where:** `LabDbContext.cs:12`
- **Fix:** `UseIdentityAlwaysColumn()`
- **Skill:** `postgresql-patterns` §1

### 19. Created response `ReceivedAt` differs from the stored value — D
- **Where:** `ResultEndpoints.cs:44`
- **Why:** the response keeps 100 ns precision, but `timestamptz` keeps microseconds, so a client using it
  as a cursor sees a different instant.
- **Fix:** truncate to microseconds before saving.

### 20. `Location` header points to `/results/{id}`, a route that doesn't exist — C
- **Where:** `ResultEndpoints.cs:44`
- **Fix:** return `TypedResults.Ok(result)` (or `Created` without a URI). Adding the route is also fine.
  The body stays the same.

### 21. Missing settings fail late with a null reference — C
- **Where:** `Program.cs:21-22` (the `!` on config)
- **Fix:** bind `Labs`/`Sms`/`Auth` to options with validation and `ValidateOnStart()`.
- **Skill:** `dotnet-core`

### 22. Unused index on `PatientId` — D
- **Where:** `LabDbContext.cs:21`
- **Fix:** keep it only if a per-patient read is coming; otherwise drop it (it only costs writes).

### 23. A DTO is used as the log category — C
- **Where:** `ResultEndpoints.cs:31` (`ILogger<RecordResultRequest>`)
- **Fix:** use a real category type, such as the endpoints class.

---

## Not fixed: decisions and open questions
- **`GET /results` returns every patient's results to any clinician, and any clinician can look up any
  national ID (C, S, D).** Settled: keep it for now. `task.md` fixes the route and its shape, and the
  service has no care-team or tenant model to filter by. Ask the product owner. Meanwhile, add a PHI
  read audit for both routes (`healthcare-compliance`), which changes no contract.
- **Migrations (D):** none in the repo, and nothing calls `Migrate()`, which is good. For production,
  generate an up/down script pair. Put the index change from 10 in its own `CONCURRENTLY` script
  (`IsCreatedConcurrently()`, no `--idempotent`, `SET lock_timeout` first). Changing `notes` from `text`
  to `varchar(1000)` on an existing table rewrites and locks it, so check for oversized rows first.
- **Forwarded headers / nginx:** not reviewed. Nothing says what sits in front of the service (C).

## What the reviews got right (C, S, D)
`TimeProvider` comes from DI. The fallback policy denies by default, and `/health/live` is explicitly
anonymous. Reads use `AsNoTracking`, and the page size is clamped. Cursors use the invariant culture.
Entities are sealed and created through a factory, with `int` keys. Nothing calls `Migrate()` or
`EnsureCreated()`. The worker opens a scope for each message.

## Not checked by any reviewer
No build, no tests, no NuGet audit, no query plans (no database). The PostgreSQL behaviour is inferred
from Npgsql. The Roslyn MCP wasn't available, so all files were read directly. None of the reviewers
found text in the repo addressed to an AI reviewer.
