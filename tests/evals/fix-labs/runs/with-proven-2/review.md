# LabResults: consolidated review

Sources: **C** `review-code.md`, **S** `review-security.md`, **D** `review-dba.md`, **K** `scan.md` (known-traps).
Paths are relative to `LabResults/`. Nothing in the service was changed. **Settled:** lines decide where the sources disagreed. Every fix below keeps what `task.md` says must stay: `Program`, the four routes and their shapes, `LabDbContext` as the schema source, typed `LabClient`/`SmsClient`, `TimeProvider` from DI, JWT bearer on the default scheme (`Auth:Authority`, audience `lab-results`), and the three settings.

25 findings: 4 critical, 8 high, 8 medium, 5 low.

## Critical

### 1. Every clinician gets 403: the role claim doesn't exist (C S D K)
`Program.cs:32` (`RoleClaimType = "realm_access.roles"`), used by the policy at `Program.cs:37`. Keycloak puts the roles inside the JSON claim `realm_access`, so `RequireRole("clinician")` never matches and all three clinician routes return 403.
**Fix:** map the roles out of `realm_access` (and `resource_access["lab-results"]`) into the role claim type in `OnTokenValidated` or an `IClaimsTransformation`. If you set `MapInboundClaims = false`, update `CurrentUser.cs:8` too, since it reads `NameIdentifier` first. Skill: `auth-patterns` §1 / Keycloak.
**Settled:** critical, not K's "warning": no clinician can use the service.

### 2. `recordedBy` is the first caller's `sub` on every result (C S D, hidden by K's row)
`Program.cs:13-17`, `Infrastructure/AuditInterceptor.cs:8,17`, `Infrastructure/CurrentUser.cs:8-10`. `AddDbContextPool` builds its options once, from the root provider. That captures one "scoped" `AuditInterceptor` and one `CurrentUser`, and `Id` is read in the constructor. So every result is stamped with whoever made the first database request, or `"system"`. That is a false clinical audit trail, and it breaks the `recordedBy` = `sub` contract.
**Fix:** make the interceptor stateless and have it read the caller from the context it is saving. Add an unmapped `CallerId` to `LabDbContext`, register `AddPooledDbContextFactory<LabDbContext>`, and add a scoped `LabDbContext` that rents from the factory and sets `CallerId = currentUser.Id`. `LabDbContext` stays the injected type and the model doesn't change. Also override the sync `SavingChanges` (C). Skill: `efcore-patterns` §1, §4.
**Settled:** use D's pooled factory with the caller set on each rental, not C's `AddDbContext` + scoped interceptor or C/S's "pass `sub` into `LabResult.Record`". It keeps pooling, keeps one place that stamps the audit fields, and it is the pattern the cited skill tests.

### 3. One SMS timeout stops the whole API (C S D)
`Application/NotificationQueue.cs:30` catches only `HttpRequestException`. An `HttpClient` timeout throws `TaskCanceledException`, which escapes `ExecuteAsync`, and the default `BackgroundServiceExceptionBehavior.StopHost` stops the host.
**Fix:** `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)`, and log the exception type and status only, never the phone or the text. Skills: `worker-patterns`, `pii-masking`.

### 4. Scope validation is off in Production, which hides #2 (C S D K)
`Program.cs:9`. Production skips `ValidateScopes`/`ValidateOnBuild`, so the captive dependency in #2 never throws.
**Fix:** `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })` in every environment. **Do #2 first:** with validation on and the interceptor unchanged, the first database call throws "cannot resolve scoped service from root provider" (S). Skill: `dotnet-core`.
**Settled:** raised from K's "info" to critical as part of #2 (C, D). It is the guard that would have caught the worst bug here.

## High

### 5. National ID written to the logs in clear on every lookup (C S D)
`Infrastructure/LabClient.cs:11` puts the national ID in the URL path. `IHttpClientFactory` logs the full URI at Information, and .NET redacts only the query string. `appsettings.json:16-17` raises only `Microsoft.AspNetCore` to Warning, so `System.Net.Http.HttpClient.*` stays at Information. Failures repeat the URI in the exception text.
**Fix:** `.RemoveAllLoggers()` on the `LabClient` registration (`Program.cs:21`), and log your own line with the status code only. Skills: `pii-masking`, `healthcare-compliance`.
**Settled:** high, not C's critical: it leaks to people who can read logs but doesn't break the service. C's "ask the lab to take the ID in a POST body" is out: `LABS.md` defines only `GET {base}/patients/{nationalId}/orders`. (C's line 17 and S's line 16 differ; it's line 16 that leaves `Default` at Information.)

### 6. National ID masking fails open (C S D)
`Application/Masking.cs:9-11`, used at `Api/ResultEndpoints.cs:41`. The regex masks only a run of exactly 10 digits: `"1012 345 678"` passes through unchanged, and `"10123456789"` becomes `******67891`, which leaks a digit.
**Fix:** log `PatientId` instead of the national ID. If a masked ID is still needed, mask everything except the last 4 characters, whatever the format. Skill: `pii-masking`.
**Settled:** high (S), not C's warning: it's the same PHI-in-logs class as #5.

### 7. `nationalId` isn't validated, so `..` reaches other lab endpoints (C S)
`Api/ResultEndpoints.cs:63-64`, `Infrastructure/LabClient.cs:11`. `Uri.EscapeDataString` leaves `.` alone, so `".."` resolves `{base}/patients/../orders` to `{base}/orders`, a call made with the service's credentials. A null value throws, which returns a 500.
**Fix:** validate `^[12][0-9]{9}$` before the call and return 400 otherwise. Skill: `owasp-aspnetcore` (SSRF/A05); validation per `rules/security.md`.
**Settled:** high (S), not C's critical: the caller must be an authenticated clinician, and the call is limited to GETs under the lab base address.

### 8. Any clinician can read every patient's results, and nobody records who read what (C S D)
`Api/ResultEndpoints.cs:48-61` (`GET /results`) and `:63` (`POST /lab-orders/lookup` for any national ID).
**Fix:** filter `GET /results` to the caller (`RecordedBy == sub`), and write an access-audit row for each read and lookup (who, whose, when). Rate-limit the lookup per caller so it can't be used to enumerate IDs (see #17). Skills: `api-design` §11, `healthcare-compliance`; `rules/security.md` (ownership).
**Settled:** filter by `RecordedBy == sub` (C), instead of leaving the decision open (S). `task.md` fixes the response shape (`{ items, next }`, the item fields, newest first, `before`), not that every clinician sees every patient, and `recordedBy` is the only ownership data the service has. Widen it to a care-team check when that data exists. Risk: if the hidden tests read results as a different clinician from the one who posted them, this filter will fail them. Nobody could check that here.

### 9. SMS is lost on restart, a full queue blocks the POST, and retries duplicate results (C S D)
`Api/ResultEndpoints.cs:40-43`, `Application/NotificationQueue.cs:10,14`. The result is committed, then the SMS goes into an in-memory bounded channel (1000). A restart or crash loses the queue. A cancelled request after the save loses that one SMS. A full channel makes `WriteAsync` block the request after the commit, and a client retry then stores a duplicate, because there's no idempotency key.
**Fix:** write an outbox row in the same `SaveChanges` as the result, and have the worker relay from the table. The outbox entity goes in `LabDbContext`, so the tests' schema picks it up. Skill: `outbox` §1.
**Settled:** high. That's between C's critical and S's medium/D's warning: the result itself is never lost, but the notification and the request's latency are.

### 10. `Notes` is an unbounded `text` column (C S D)
`Domain/LabResult.cs:19`, with no `HasMaxLength` in `Infrastructure/LabDbContext.cs:12-22`, and nothing validated at `Api/ResultEndpoints.cs:33`. This is a hard ban in `rules/efcore-rules.md`, so it blocks merge.
**Fix:** `HasMaxLength(1000)` and the same limit in the request validator. Skill: `efcore-patterns` §2.
**Settled:** 1000 (D), not 2000 (S) or "≤ 4000" (C). The house rule says to size it smaller when in doubt, because raising it later is a cheap reviewed `ALTER`. High, not C's critical: it breaks no request.

### 11. NuGet vulnerability audit isn't enforced, and it wasn't run (S)
`LabResults.csproj`: no `NuGetAudit`/`NuGetAuditMode=all`, and NU1901-NU1904 aren't errors.
**Fix:** add them in a `Directory.Build.props`, then run `dotnet list package --vulnerable --include-transitive`. Any hit is a hard fail. Skill: `zero-vulns`.

### 12. Request input is never validated (C S D)
`Api/ResultEndpoints.cs:33`. `Phone`, `NationalId`, `PatientId`, `Notes` and `Value` aren't checked. An unchecked phone lets any clinician send SMS to any number (toll fraud). `Value` of 10,000,000 or more overflows `numeric(10,3)` (`LabDbContext.cs:17`, Postgres 22003) and returns 500, and extra decimal places are rounded silently. Nothing checks that the national ID belongs to the patient.
**Fix:** a FluentValidation validator: phone in E.164 `+9665…` form (the format in `LABS.md`), the national ID as in #7, `Value` range and scale within `numeric(10,3)`, `Notes` ≤ 1000. Return 400 on failure. Skill: `api-design` (validation); `rules/security.md`.
**Settled:** high, not medium (C, S, D): the SMS toll-fraud path makes it more than a 500.

## Medium

### 13. Paging skips rows that share a timestamp (C S D)
`Api/ResultEndpoints.cs:55-59`. The query orders by `ReceivedAt` alone with a strict `<`, so ties at a page edge are lost. The `"O"` cursor contains `+00:00`, and an unencoded `+` comes back as a space.
**Fix:** order by `ReceivedAt DESC, Id DESC` and filter `received_at < @t OR (received_at = @t AND id < @id)`. Make `next` an opaque URL-safe token (base64url of `"{O}|{id}"`). The `{ items, next }` and `before` shape stays. Index: `HasIndex(r => new { r.ReceivedAt, r.Id })` (`ix_lab_results_received_at_id`, DESC), replacing `HasIndex(ReceivedAt)` at `LabDbContext.cs:20`. Skill: `efcore-patterns` §9.

### 14. A bad or non-UTC `before` returns 500 (C S D)
`Api/ResultEndpoints.cs:54`. `DateTimeOffset.Parse` throws `FormatException` on bad input, and Npgsql rejects a non-zero offset for `timestamptz`, so `…+03:00` fails too.
**Fix:** `TryParse` with `CultureInfo.InvariantCulture` and `DateTimeStyles.AdjustToUniversal`, return 400 on failure, and use `.ToUniversalTime()`. This merges into the token parsing in #13. Skills: `efcore-patterns` §9, `postgresql-patterns` §2.

### 15. Errors end as bare 500s with no ProblemDetails (C S)
`Program.cs:39-44`. There's no `AddProblemDetails`/`UseExceptionHandler`, so a lab 404/5xx, a database overflow or a bad cursor becomes a bare 500 with no trace id.
**Fix:** `AddProblemDetails()`, `UseExceptionHandler()`, `UseStatusCodePages()`. In `LabClient.LookupAsync`, map a lab 404 to an empty list and an outage to a 502 ProblemDetails. Skill: `api-design`.
**Settled:** medium, not S's high: nothing leaks in Production, the responses are just unhelpful.

### 16. HTTP clients have no timeout or resilience (C S)
`Program.cs:21-22`. Both typed clients run on the 100 s default with no retry policy.
**Fix:** `AddStandardResilienceHandler()` with a short attempt timeout. The standard handler doesn't retry the SMS POST. Skill: `polly-resilience`.

### 17. No security headers, HSTS, forwarded headers or rate limiting (S, C)
`Program.cs:41`. No `nosniff`, frame options, CSP or HSTS. No `UseForwardedHeaders` behind nginx (C). No rate limit on `/lab-orders/lookup`, which can be used to enumerate national IDs.
**Fix:** add the headers on the error path too, `UseHsts()`, `UseForwardedHeaders()` with known proxies, and a per-caller limiter on the lookup. Middleware order per `rules/aspnetcore-rules.md`. Skills: `owasp-aspnetcore`, `nginx` §7.

### 18. PHI in the SMS text (C D)
`Api/ResultEndpoints.cs:43`. The message names the test (e.g. HbA1c), which is health data sent to a number nobody verified. The Arabic text is hard-coded.
**Fix:** generic text ("a new result is ready"), moved to resources. Skills: `healthcare-compliance`, `localization` §13.

### 19. JWT `ClockSkew` left at 5 minutes (C S)
`Program.cs:29-33`. **Fix:** `ClockSkew = TimeSpan.FromSeconds(30)`. Source: `rules/security.md`.

### 20. No migration or reviewed schema scripts (D)
Production has no way to get the schema. **Fix:** add an initial migration and commit the reviewed up and down scripts for the pipeline. Never call `Migrate()`/`EnsureCreated()`; the app doesn't today, and the tests create the schema from the model, so this doesn't affect them. Use `UseIdentityAlwaysColumns()` for the key (D info, `postgresql-patterns` §4). Skills: `efcore-patterns` §10, `rules/efcore-rules.md`.

## Low

### 21. Missing settings fail at first use, not at start-up (C)
`Program.cs:21-22, 27`. The `!` hides a missing `Labs:BaseAddress`/`Sms:BaseAddress`/`Auth:Authority`.
**Fix:** bind options with `ValidateDataAnnotations().ValidateOnStart()`, keeping the same setting names. Skill: `dotnet-core`.

### 22. `CurrentUser` falls back to `"system"` (S)
`Infrastructure/CurrentUser.cs:10`. A missing identity is stamped as `"system"` instead of failing.
**Fix:** fail closed (throw or 401) inside a request. This goes with #2.

### 23. `Location` points to a route that doesn't exist (C S)
`Api/ResultEndpoints.cs:44`. `/results/{id}` isn't mapped. **Fix:** return `TypedResults.Ok(result)` or a 201 without that URI. Keep the body, since the tests expect the result back.

### 24. Layering and naming (C)
- `Application/NotificationQueue.cs:2,28`: Application references `Infrastructure.SmsClient`. Add `ISmsSender` in Application (`rules/architecture.md`).
- `Api/ResultEndpoints.cs:31`: logger category is `ILogger<RecordResultRequest>`, a DTO.
- `Api/ResultEndpoints.cs:63`: the infrastructure type `LabOrder` is returned from the API. Map it to an API DTO with the same JSON shape.

### 25. Database housekeeping (D)
- `appsettings.json:3`: no `Maximum Pool Size` or timeouts. Agree `pods × pool` under `max_connections` (100 by default) with the DBA. The password correctly comes from a secret, not the file (D, S). Skill: `postgresql-patterns` §3.
- `LabDbContext.cs:21`: `ix_lab_results_patient_id` serves no query today. Keep it only if a read by patient is planned.
- `EnableRetryOnFailure` is safe to add, because the code opens no transactions of its own.

## What the reviewers got right (C S D)
Fallback policy denies by default. `TimeProvider` from DI, `AsNoTracking` on reads, and `CancellationToken` passed through. Sealed classes, private setters, bounded `Test`/`Unit`/`RecordedBy`, `int` key. No password in source. No text in the repo addressed to an AI reviewer (C S D).

## Not checked by anyone
- The build, the tests, and `dotnet list package --vulnerable` (#11).
- Behaviour against real Keycloak tokens (#1) or PostgreSQL (#13, #14, query plans).
- How the hidden tests sign tokens and which clinician reads results (affects #1 and #8).
