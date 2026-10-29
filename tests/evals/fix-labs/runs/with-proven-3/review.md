# LabResults: consolidated review

Sources: **C** = `review-code.md`, **S** = `review-security.md`, **D** = `review-dba.md`, **K** = `scan.md` (known-traps scan).

**Status: nothing here is applied yet.** All three reviewers were read-only. They proposed every fix below and edited nothing. No one ran the build, the tests or `dotnet list package --vulnerable`. None of the reviews found text in the repo aimed at an AI reviewer (S, D).

**Verdict:** not ready for production. Four critical issues: every clinician gets a 403, results are stamped with the wrong clinician, one SMS timeout stops the host, and national IDs are logged in clear.

**What the fixes must keep (`task.md`):** `Program`; the four routes and their request and response shapes; `LabDbContext` (the tests build the schema from its model); `LabClient` and `SmsClient` as typed `HttpClient`s; `TimeProvider` from DI; JWT bearer on the default scheme with authority `Auth:Authority` and audience `lab-results`; the settings `ConnectionStrings:Lab`, `Labs:BaseAddress` and `Sms:BaseAddress`.

---

## Critical

### 1. Keycloak roles never reach `RequireRole`, so every clinician gets 403 (C, S, D, K)
- **Where:** `Program.cs:32` (`RoleClaimType = "realm_access.roles"`), `Program.cs:29-33`, `Program.cs:37`
- **Problem:** Keycloak puts roles inside the `realm_access` JSON claim. No claim is named `realm_access.roles`, so `RequireRole("clinician")` fails on every route. Also: `ClockSkew` is left at the 5-minute default and `MapInboundClaims` isn't set.
- **Fix:** `MapInboundClaims = false`, `RoleClaimType = "roles"`, and an `OnTokenValidated` handler that copies roles from `realm_access` and `resource_access["lab-results"]` into role claims. Also set `ClockSkew = 30s` and `RequireHttpsMetadata = true`. Keep the default scheme, `Auth:Authority` and audience `lab-results`.
- **Skill:** `auth-patterns` §1 (`AddKeycloakJwt`), `rules/security.md`
- **Disagreement settled:** K rated this a *warning*. It's **critical**, because the service refuses every real token.
- **Note:** `MapInboundClaims = false` also keeps `sub` under its own name, and `recordedBy` (finding 2) needs that.

### 2. A captive scoped interceptor stamps every result with the first caller's `sub` (C, S, D, K)
- **Where:** `Program.cs:14-17`, `AuditInterceptor.cs:8,17`, `CurrentUser.cs:8-10`, plus `Program.cs:9` (scope validation off: K row 2)
- **Problem:** `AddDbContextPool` builds its options once, from the root provider. The scoped `AuditInterceptor` and its `CurrentUser` are therefore resolved once, and `CurrentUser.Id` is read in the constructor. Until the process restarts, every row's `RecordedBy` is the first clinician's `sub` (or `"system"`). The audit trail is wrong and nothing reports an error. This also breaks the `recordedBy` contract in `task.md`. Production skips scope validation, so nothing throws.
- **Fix:** make the interceptor stateless and a singleton that reads `CallerId` from `eventData.Context`. Add `CallerId` to `LabDbContext`. Register `AddPooledDbContextFactory<LabDbContext>` plus a scoped `LabDbContext` that sets `CallerId` from the caller's `sub` on every rental. Read claims lazily. Refuse the write when there's no `sub` instead of recording `"system"` (S). Then turn on `ValidateScopes` and `ValidateOnBuild` in every environment.
- **Skill:** `efcore-patterns` §1, §4, §11; `dotnet-core` §1
- **Disagreements settled:**
  - Which fix: C offered "pass `sub` into `LabResult.Record` and drop the interceptor", S offered `AddDbContext`, and D gave the pooled-factory design. **Use the pooled factory (D, also offered by C and S).** It keeps pooling and the audit stays in one place, and `LabDbContext` stays the type the tests build the schema from.
  - K rated row 2 *info*. It's **critical** here because it hides this bug. Ship it **together with** the factory fix: on its own, every request fails (D).

### 3. One SMS timeout stops the whole host (C, S, D)
- **Where:** `NotificationQueue.cs:30`
- **Problem:** the worker catches only `HttpRequestException`. A `TaskCanceledException` from the `HttpClient` timeout, or any other exception, escapes `ExecuteAsync`, and the default `BackgroundServiceExceptionBehavior.StopHost` takes the API down.
- **Fix:** `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)` and log the exception type only (no message text, which may contain the phone number). Add a `StopAsync` that drains the queue on shutdown, `FullMode = Wait`, and a client timeout (see finding 13).
- **Skill:** `worker-patterns` ("An in-process queue that drains on shutdown"), `polly-resilience`

### 4. The national ID goes into the logs through `HttpClient` request logging (C, S, D)
- **Where:** `LabClient.cs:11` (the ID is in the URL path), `Program.cs:21`, `appsettings.json:15-17`
- **Problem:** `IHttpClientFactory` logs the full request URI at Information. Only `Microsoft.AspNetCore` is raised to Warning, so every lookup logs the ID in clear, and failures log the exception text too. `SmsClient` has the same problem with phone numbers.
- **Fix:** call `.RemoveAllLoggers()` on the `LabClient` and `SmsClient` builders, and log your own line with the status code only.
- **Skill:** `pii-masking` ("HttpClient logs a failed request")
- **Disagreement settled:** D suggested raising `System.Net.Http.HttpClient` to Warning instead. **Use `RemoveAllLoggers` (C, S).** A level in config can be lowered per environment, and failures still log the URI at Warning or above.

---

## High

### 5. The unvalidated national ID lets callers change the lab API path, and `null` causes a 500 (C, S)
- **Where:** `ResultEndpoints.cs:63-64`, `LabClient.cs:11`
- **Problem:** `Uri.EscapeDataString` leaves `.` alone, so `".."` becomes `patients/../orders`, which resolves to `{base}/orders` on the lab host. A `null` value throws, which gives a 500.
- **Fix:** validate before the handler: convert Arabic-Indic digits to Western ones, require `^[12]\d{9}$`, and return `ValidationProblem` otherwise. The `{ nationalId }` body stays the same.
- **Skill:** `api-design` §3

### 6. Unvalidated phone numbers allow SMS pumping, and the message carries PHI (C, S, D)
- **Where:** `ResultEndpoints.cs:33`, `ResultEndpoints.cs:43`
- **Problem:** `phone` isn't validated or matched to the patient, and the SMS names the test (e.g. HbA1c). Any clinician token can send health data to any number, or run up SMS costs.
- **Fix:** validate the mobile number in E.164 form (`+9665…`). Send a generic "your result is ready" text from a resource, not a literal. Rate-limit per caller.
- **Skill:** `api-design` §3, §6; `healthcare-compliance`
- **Disagreement settled:** S preferred taking the number from the patient's record. **Validate the format instead (C).** `task.md` keeps `phone` in the `POST /results` body, and the service has no patient store to look it up in. Flag the patient-record lookup to the owners.

### 7. Paging silently skips results that share a timestamp (C, S, D)
- **Where:** `ResultEndpoints.cs:54-55,58-59`, `LabDbContext.cs:20` (index)
- **Problem:** the cursor is `ReceivedAt` alone with a strict `<`, so results with the same timestamp at a page edge are dropped. Also:
  - `DateTimeOffset.Parse` throws on bad input, which gives a 500.
  - A cursor with a non-zero offset is rejected by Npgsql for `timestamptz`.
- **Fix:**
  - Sort with `OrderByDescending(ReceivedAt).ThenByDescending(Id)`.
  - Filter with `r.ReceivedAt < c.At || (r.ReceivedAt == c.At && r.Id < c.Id)`.
  - Make `next` an opaque string carrying both values. It is still passed back as `before`, so the `{ items, next }` contract doesn't change.
  - Parse the cursor with `TryParse`; on failure return a 400. Call `.ToUniversalTime()` on the timestamp.
  - Replace the index: `HasIndex(r => new { r.ReceivedAt, r.Id }).IsDescending(true, true)`.
- **Skill:** `efcore-patterns` §9
- **Disagreement settled:** the reviewers rated this critical (D), warning (C) and medium (S). It's **high**: data is lost silently, but it can be fixed without changing the route shape.

### 8. `Notes` is an unbounded `text` column with no length check (C, S, D)
- **Where:** `LabDbContext.cs:12-22` (no config for `Notes`), `LabResult.cs:19`, `ResultEndpoints.cs:33`
- **Fix:** `HasMaxLength(1000)`, and return a 400 above that length in `RecordAsync`.
- **Skill:** `rules/efcore-rules.md` (hard ban), `efcore-patterns` §2
- **Disagreement settled:** C said "≤ 4000" and D said 1000. **Use 1000 (D).** The house rule says "when in doubt, size it smaller", and raising the limit later only takes a reviewed `ALTER`.

### 9. The national ID is written in the app's own log line, and the masking misses many shapes (C, S)
- **Where:** `ResultEndpoints.cs:41`, `Masking.cs:9-11`
- **Problem:** `[0-9]{10}` misses 9-digit IDs, IDs with spaces or dashes, and Arabic-Indic digits. An 11-digit ID leaks its last digit. `Masking.NationalId(null)` throws *after* `SaveChangesAsync`, so the client gets a 500, the row is saved, and no SMS is sent.
- **Fix:** log `PatientId` instead and drop the national ID from the log line.
- **Skill:** `pii-masking` ("Arabic-Indic digits")
- **Disagreement settled:** C also offered masking by length. **Log `PatientId` (S, and C's first choice).** The ID isn't stored anywhere, so there's nothing to mask.

---

## Medium

### 10. Missing request validation turns bad input into 500s (C, S, D)
- **Where:** `ResultEndpoints.cs:33-41`, `LabDbContext.cs:17`
- **Problem:** a `Value` of 10,000,000 or more overflows `numeric(10,3)` and comes back as a database error. `PatientId` isn't checked either.
- **Fix:** one validator per request DTO, covering `Value` within `numeric(10,3)`, `PatientId > 0`, and the rules from findings 5, 6 and 8.
- **Skill:** `api-design` §3

### 11. No ProblemDetails, global exception handler, security headers or HSTS (C, S)
- **Where:** `Program.cs:39-44`
- **Problem:** an upstream 404 from the lab (`GetFromJsonAsync` throws) comes back as a bare 500.
- **Fix:**
  - `AddProblemDetails` and `UseExceptionHandler`.
  - Set security headers in `OnStarting`, so they're also sent on errors.
  - HSTS and ForwardedHeaders.
  - Map the lab's 404 to an empty list.
- **Skill:** `api-design` §2, `owasp-aspnetcore` §1-2

### 12. SMS is queued in memory after the save: lost on restart, and retries create duplicates (C, S, D)
- **Where:** `ResultEndpoints.cs:40-43`, `NotificationQueue.cs:10`
- **Problem:** a pod restart loses queued SMS. When the queue is full (1,000 messages), the POST blocks after the save, and a client retry stores a duplicate result.
- **Fix:** write an outbox row in the same `SaveChangesAsync` and send it from a relay. Finding 3 is the minimum fix if this one waits.
- **Skill:** `outbox`, `efcore-patterns` §5

### 13. No timeouts or resilience on the typed clients, and config is only checked at first use (C, S, D)
- **Where:** `Program.cs:21-22,27`
- **Fix:** `AddStandardResilienceHandler()` on both clients (no retries on the SMS POST). Replace the `!` on config values with validated options and `ValidateOnStart()`. Keep the setting names `Labs:BaseAddress`, `Sms:BaseAddress` and `Auth:Authority`.
- **Skill:** `polly-resilience`, `dotnet-core`

### 14. Any clinician can read every patient's results, and reads aren't audited or rate-limited (C, S)
- **Where:** `ResultEndpoints.cs:48-61,63`
- **Fix:** keep the route shapes. Add an audit event for each read and a per-caller rate limit on the lookup. Ask the owners whether care-team scoping is needed.
- **Skill:** `auth-patterns` §4, `api-design` §6
- **Settled:** **flag this to the owners; don't change it.** Filtering `GET /results` by caller would change the contract the hidden tests use.

### 15. Database connection settings (S, D)
- **Where:** `appsettings.json:3`
- **Problem and fix:**
  - No `SSL Mode`, so Npgsql defaults to `Prefer`, which can fall back to plain text. Set `SSL Mode=VerifyFull` (S).
  - No pool size or timeouts, so 100 connections per pod times the number of pods can pass PostgreSQL's `max_connections` of 100. Set `Maximum Pool Size=20;Timeout=15;Command Timeout=30`, sized for the fleet (D).
  - Add `EnableRetryOnFailure(3)`. That's safe here because the service opens no transactions of its own (D).
- **Keep:** the key name `ConnectionStrings:Lab`.
- **Skill:** `postgresql-patterns` §3

### 16. No dependency-vulnerability gate (C, S)
- **Where:** `LabResults.csproj`
- **Fix:** `NuGetAudit` with `NuGetAuditMode=all`, `NU1901`-`NU1904` as errors, and `TreatWarningsAsErrors` for the nullable warnings. Then run the vulnerable-package scan, which no reviewer could run. This gate is still open.
- **Skill:** `zero-vulns`

---

## Low

17. **The key is `BY DEFAULT` identity, but the house rule is `ALWAYS`** (D). `LabDbContext.cs:12`. Fix: `e.Property(r => r.Id).UseIdentityAlwaysColumn()`. Skill: `postgresql-patterns` §4.
18. **The list loads whole entities and maps them in memory** (D). `ResultEndpoints.cs:58`. Fix: project with `.Select(r => new ResultResponse(...))`. Skill: `efcore-patterns` §9.
19. **Layering** (C). `NotificationQueue.cs:2` (Application references Infrastructure's `SmsClient`), the endpoints use `LabDbContext` directly, and the API returns Infrastructure's `LabOrder` (`LabClient.cs:5`). Skill: `rules/architecture.md`. Change these only behind the shapes `task.md` keeps.
20. **The `Location` header points at `/results/{id}`, which doesn't exist** (C). `ResultEndpoints.cs:44`. Fix: keep returning the result, but drop or correct the header.
21. **A DTO is used as the log category** (C). `ResultEndpoints.cs:31` uses `ILogger<RecordResultRequest>`. Fix: use `ILoggerFactory` with a named category.

---

## Migration note (D)
The repo has no migrations, and the app never calls `Migrate()` or `EnsureCreated()`, which is correct. Before production:
- Generate a reviewed up/down script pair for the changes above.
- Script the new composite index (finding 7) on its own, with `CREATE INDEX CONCURRENTLY` (`IsCreatedConcurrently()`) and without `--idempotent`.
- Start that script with `SET lock_timeout`.

## Done well (C)
- Deny-by-default fallback policy, with only `/health/live` anonymous.
- `TimeProvider` comes from DI, and `InvariantCulture` is used for the cursor.
- The other strings are bounded, and `numeric` and `timestamptz` are explicit.
- The list query uses `AsNoTracking`, the cancellation token is passed all the way down, and the classes are sealed.

## Not checked by anyone
- The build, the tests, and the vulnerable-package scan.
- Behaviour with real Keycloak tokens.
- `EXPLAIN` on the list query (there's no database here).
- `RequireHttpsMetadata = true` (finding 1) assumes the hidden tests' token setup doesn't fetch metadata over plain HTTP. Check this when the fix is applied.
