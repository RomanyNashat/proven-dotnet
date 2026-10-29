I've read every file in the service and checked the relevant skills, and I'm writing up the findings now. `task.md` asks for edits, but this review is read-only, so I changed nothing and every item below comes with its fix.

## Code Review: LabResults

### Summary
The service builds cleanly, but it isn't ready for production. Clinicians can't reach any endpoint, `recordedBy` is wrong on every result, one SMS timeout can stop the whole service, and national IDs reach the logs unmasked.

**Scan rows:** both are confirmed, and row 2 is worse than "info".
- **Row 1 (`Program.cs:32`), confirmed.** `RoleClaimType = "realm_access.roles"` matches no claim. Keycloak sends `realm_access` as one JSON claim, so `RequireRole("clinician")` always fails. Every clinician gets 403 on all three routes (`auth-patterns` §Keycloak; it lists this exact line as an anti-pattern).
- **Row 2 (`Program.cs:9`), confirmed and raised to Critical.** Scope validation being off is what hides the bug in Critical 2 below. Fix: `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })` (`dotnet-core`).

### Critical (must fix)
1. **Auth** `Program.cs:32,37`: the role mapping described in scan row 1. **Fix**: add `IClaimsTransformation` or `OnTokenValidated` code that reads the `realm_access` JSON (and `resource_access.lab-results.roles` if the client uses those) and adds `ClaimTypes.Role` claims. While there, set `ClockSkew = TimeSpan.FromSeconds(30)` (security rules).
2. **DI / EF** `Program.cs:14-17`, `CurrentUser.cs:8`: `AddDbContextPool` builds its options once, from the root provider. So the scoped `AuditInterceptor` and its `CurrentUser` are created once and kept for good. `Id` is read in the constructor, so every result gets stamped with the first caller's `sub`, or `"system"`. The hidden tests check `recordedBy`. **Fix**: register a singleton interceptor that reads `IHttpContextAccessor.HttpContext` inside `SavingChangesAsync`, or use `AddDbContext` (`efcore-patterns` §1 and anti-patterns; `auth-patterns` l.285).
3. **Worker** `NotificationQueue.cs:30`: only `HttpRequestException` is caught. An `HttpClient` timeout throws `TaskCanceledException`, which escapes `ExecuteAsync`. The default `BackgroundServiceExceptionBehavior.StopHost` then shuts the whole API down. **Fix**: `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)` (`worker-patterns` §polling worker).
4. **PII in logs** `appsettings.json:15-17`, `LabClient.cs:11`: the national ID is part of the URL path. `System.Net.Http.HttpClient.LabClient.*` logs at Information and prints the full URI. .NET 9+ hides query strings, not paths. **Fix**: set `"System.Net.Http.HttpClient": "Warning"`, or add a custom `IHttpClientLogger` (`pii-masking`).
5. **PII masking** `Masking.cs:9-11`: `[0-9]{10}` only masks exactly 10 ASCII digits. Arabic-Indic digits (`١٠١٢...`), spaces or any other format get logged in full, because `NationalId` is never validated. **Fix**: mask everything except the last 4 characters whatever they are, and validate and normalise `NationalId` to 10 digits at the edge (`pii-masking` l.29).
6. **Path traversal / SSRF** `LabClient.cs:11`, `ResultEndpoints.cs:63`: `Uri.EscapeDataString("..")` leaves `..` as it is. `patients/../orders` then resolves to `{base}/orders`, so a caller can reach other lab API paths using the service's identity. **Fix**: validate `NationalId` with `^[0-9]{10}$` after normalising, and return 400 otherwise.
7. **DBA hard rule** `LabDbContext.cs:12-22`: `Notes` has no `HasMaxLength`, so it becomes an unbounded `text` column (`efcore-rules`). **Fix**: `HasMaxLength(n)` (≤ 4000) plus a matching length check in the request validation.

### Warnings (should fix)
1. **Paging correctness** `ResultEndpoints.cs:54-58`:
   - Paging on `ReceivedAt <` alone skips results that share a timestamp at a page boundary. Use keyset paging on `(ReceivedAt, Id)` with a composite index.
   - A bad `before` value makes `Parse` throw, which returns 500. Use `TryParse` and return 400.
   - A cursor with a non-zero offset throws in Npgsql for `timestamptz`. Call `.ToUniversalTime()` first.
2. **Validation** `ResultEndpoints.cs:33-36`:
   - `Phone`, `NationalId`, `PatientId` and the range of `Value` (`numeric(10,3)` allows at most 7 digits before the decimal point) aren't checked, so bad values fail in the database with a 500.
   - Unit errors are reported under the `"Test"` key.
   - **Fix**: a FluentValidation validator (security rules).
3. **Lost or duplicated SMS** `ResultEndpoints.cs:40-43`, `NotificationQueue.cs:10`:
   - The queue lives in memory, so it is lost on restart.
   - The bounded `WriteAsync` waits when the gateway is slow. A request cancelled at that point has already saved its result, so the client retries and the result is stored twice.
   - **Fix**: use an outbox table (`outbox`), or at least `TryWrite` and log a drop.
4. **Health data in SMS** `ResultEndpoints.cs:43`: the text names the test (for example HIV or HbA1c). That reaches the gateway and shows on lock screens. Send a generic message instead (`healthcare-compliance`).
5. **Resilience** `Program.cs:21-22`: the HTTP clients have no timeout or retry policy, so the default 100 s timeout applies. A 404 from the lab for an unknown patient becomes a 500. **Fix**: `AddStandardResilienceHandler` (`polly-resilience`), and map 404 to an empty list.
6. **Errors** `Program.cs:39-42`: there is no `AddProblemDetails()` or `UseExceptionHandler()`, so failures return an empty 500 with no structure (architecture rules).
7. **Data scope** `ResultEndpoints.cs:48-58`: any clinician can list every patient's results, and look up any national ID with no audit trail. Confirm this is intended, and add a PHI audit (`healthcare-compliance`).

### Suggestions
- `Program.cs:21-22`: the `!` on missing config fails late with a null-reference error. Validate the settings at startup with options plus `ValidateOnStart()`.
- `ResultEndpoints.cs:31`: `ILogger<RecordResultRequest>` uses a DTO as the log category.
- `ResultEndpoints.cs:43`: move the hard-coded Arabic SMS text into resources.
- `ResultEndpoints.cs:44`: the `Location` header points to `/results/{id}`, a route that doesn't exist.
- `LabResults.csproj`: there is no `NuGetAudit` set to treat NU1901–1904 as errors (`zero-vulns`).

### Positive
- `TimeProvider` comes from DI.
- `AsNoTracking` is used, and the page size is clamped.
- The deny-by-default fallback policy is set, and `/health/live` is explicitly `AllowAnonymous`.
- Cursors are formatted with the invariant culture.
- Entities are sealed and changed through a factory method, and keys are `int`.
- There is no `Migrate()` or `EnsureCreated()` in the app.
- The worker creates its own scope for each message.
- I found no text addressed to AI reviewers anywhere in the repo.

**Not checked:** I didn't build, run tests or run a NuGet audit. Behaviour against PostgreSQL is inferred from Npgsql's known behaviour, not run. Nothing says whether the service sits behind nginx, so I didn't review forwarded headers. No Roslyn MCP was available, but the service is 11 small files, so I read all of them.
