I found 3 critical, 6 high and 6 medium problems in `LabResults`. I also confirmed both rows in `../scan.md`. I only read the code: I had no shell, so I didn't build, test or run the dependency audit. Nothing was changed.

## Security Review: LabResults (pre-production)

### Scan rows
- **`Program.cs:32` RoleClaimType: confirmed, critical.** `realm_access.roles` is not a claim. Keycloak puts the roles inside the JSON claim `realm_access`, so `RequireRole("clinician")` never matches and every clinician gets 403 on all three routes. (`auth-patterns` §1)
- **`Program.cs:9` scope validation: confirmed, and it hides a real bug** (Critical 2 below). Fix the interceptor first, then turn validation on everywhere. Turned on alone, the first database call throws "cannot resolve scoped from root". (`dotnet-core`)

### Critical
1. **A01 / A07** `Program.cs:29-33`: map the roles out of `realm_access` (and `resource_access["lab-results"]`) in `OnTokenValidated` and set `RoleClaimType = "roles"`, as in `auth-patterns` §1. If you set `MapInboundClaims = false`, also update `CurrentUser.cs:8`, which reads `NameIdentifier` first.
2. **A09 / audit integrity** `Program.cs:13-17` + `CurrentUser.cs:8-10`: the pooled context's options are built once, from the root provider. That resolves one "scoped" `AuditInterceptor` and one `CurrentUser` for the life of the process.
   - **Impact:** `recordedBy` on every result is the `sub` of whoever made the first database request, or `"system"`. That is a false clinical audit trail, and the hidden `recordedBy == sub` check fails.
   - **Fix:** use `AddPooledDbContextFactory` plus a scoped rental that sets the caller on each rental (`efcore-patterns` §1). Simpler: pass the caller's `sub` into `LabResult.Record(...)`.
   - Then add `ValidateServicesInEveryEnvironment()`.
3. **A10 / availability** `NotificationQueue.cs:30`: only `HttpRequestException` is caught. An SMS timeout throws `TaskCanceledException` instead, which escapes `ExecuteAsync`, and the default `StopHost` shuts the whole service down.
   - **Fix:** `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)` and log the type/status only (`worker-patterns`, `pii-masking`).

### High
1. **A09 PII** `LabClient.cs:11`: the national ID goes in the URL path. `IHttpClientFactory`'s default logging writes the full URI at Information level, and `appsettings.json:16` leaves that category at Information, so every lookup logs a national ID in clear. .NET redacts only the query string, not the path.
   - **Fix:** `.RemoveAllLoggers()` on the `LabClient` registration (`Program.cs:21`) and log your own line with the status code (`pii-masking`).
2. **A09 PII** `Masking.cs:9`, used at `ResultEndpoints.cs:41`: the masking only matches exactly 10 consecutive digits, so it fails open. `"1012 345 678"` goes through unchanged, and `"10123456789"` becomes `******67891`, which leaks a digit.
   - **Fix:** log `PatientId` instead, or mask everything except the last 4 characters whatever the format.
3. **A05 / SSRF-lite** `ResultEndpoints.cs:63-64`, `LabClient.cs:11`: `nationalId` is never validated. `".."` survives `EscapeDataString`, so the call becomes `{base}/orders`. A null value throws, which returns a 500.
   - **Fix:** a validator requiring `^[12][0-9]{9}$`, returning 400 otherwise.
4. **A10** `Program.cs:39-44`: there is no `AddProblemDetails`/`UseExceptionHandler`. A lab API 404/5xx, a database overflow or a bad cursor ends as a bare 500 with no trace id.
   - **Fix:** `builder.Services.AddProblemDetails(); app.UseExceptionHandler(); app.UseStatusCodePages();`. In `LookupAsync`, map a lab 404 to an empty list or a 404, and a lab outage to 502.
5. **A01 / healthcare** `ResultEndpoints.cs:48-61`: any clinician can page through every patient's results, and nothing records who read what. The same goes for lookups of any national ID.
   - **Fix:** scope reads to the clinician's patients or care team (resource-based authorization), and write an access-audit record per read and lookup (`healthcare-compliance`). This can't be added without changing the `GET /results` contract, so the decision is yours.
6. **A03** `LabResults.csproj`: `NuGetAudit` is not enforced. There is no `NuGetAuditMode=all`, and NU1901-1904 are not errors.
   - **Fix:** add them in `Directory.Build.props` (`zero-vulns`).
   - **I couldn't run** `dotnet list package --vulnerable --include-transitive`, so whether any package is vulnerable is still unknown.

### Medium
1. **A06** `ResultEndpoints.cs:33`, `LabDbContext.cs:12-22`:
   - `Notes` has no length limit. It is unbounded in the request and becomes a `text` column (`efcore-rules` ban). Use `HasMaxLength(2000)` plus a request check.
   - `Value` is not checked against `numeric(10,3)`, so an out-of-range value is a 500 on overflow.
   - `PatientId`, `Phone` and `NationalId` are not validated, and there is no check that the national ID belongs to the patient.
2. **A10 / correctness** `ResultEndpoints.cs:54-59`:
   - `DateTimeOffset.Parse` turns a bad `before` into a 500. Use `TryParse` and return 400.
   - The cursor is `ReceivedAt` only, with a strict `<`, so rows with the same timestamp at a page edge are skipped. Use a keyset on `(ReceivedAt, Id)` with a matching index, and keep the same `next`/`before` shape.
3. **Design** `ResultEndpoints.cs:40-43`, `NotificationQueue.cs:10`: the SMS is queued in memory after the commit, so a restart loses it. A full channel blocks the POST. Use an outbox (`outbox`).
4. **A07** `Program.cs:29`: `ClockSkew` is left at the 5-minute default. Set 30 s (`rules/security.md`).
5. **A02** `Program.cs:41`: no security headers (`nosniff`, frame options, CSP), no HSTS, and no rate limiting on `/lab-orders/lookup`, which can be used to enumerate national IDs.
6. **Resilience** `Program.cs:21-22`: both HTTP clients have no timeout or resilience handler, so the default is 100 s. Use `AddStandardResilienceHandler` with a short attempt timeout (`polly-resilience`).

### Low / Informational
- `CurrentUser.cs:10`: falling back to `"system"` hides a missing identity. Fail closed instead.
- `ResultEndpoints.cs:44`: the `Location: /results/{id}` header points at a route that doesn't exist.
- `appsettings.json:3`: the connection string has no password in source, which is good. It must come from the vault or the environment.
- I found no text in the repo trying to steer an AI review.

**Not checked:** build, tests, the actual vulnerable-package list, and behaviour against real Keycloak tokens or PostgreSQL.
