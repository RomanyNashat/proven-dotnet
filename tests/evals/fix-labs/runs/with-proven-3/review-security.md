I couldn't run anything: I only had read access, so I made no edits and ran no build, tests or `dotnet list package --vulnerable`. These findings come from reading the code against the harness skills, which I cite.

The worst one: every lab result gets stamped with the first clinician who used the service after it started, so the "who recorded it" record is wrong for everyone else. The same mistake in a hidden test would break the `recordedBy` contract.

## Security Review: LabResults

**scan.md, both rows confirmed:**
- **Row 1** (`Program.cs:32`): confirmed. `RoleClaimType = "realm_access.roles"` names a claim that doesn't exist, so `RequireRole("clinician")` refuses every real Keycloak token with 403 and the service is unusable (`auth-patterns` §1).
- **Row 2** (`Program.cs:9`): confirmed, and worse than "info". The captive dependency it would catch is already in the code (Critical 1 below). Turning on `ValidateScopes` only makes it a 500 on the first database call, so the registration needs fixing too (`dotnet-core`).

### Critical
1. **A09, audit integrity** `Program.cs:14-17`, `AuditInterceptor.cs:8,17`, `CurrentUser.cs:8-10`
   - **What:** a scoped `AuditInterceptor`, holding a scoped `CurrentUser`, is resolved inside `AddDbContextPool`'s options. Those options are built once from the root provider, and `CurrentUser.Id` is read once in its constructor.
   - **Impact:** every result is recorded under the first caller's `sub` (or `"system"`) for the life of the process. The clinical audit trail is false.
   - **Fix:** make the interceptor stateless and read the caller from the context. Use `AddPooledDbContextFactory` plus a scoped `LabDbContext` that sets `CallerId` each time one is rented (`efcore-patterns` §1 and §4). Or simply use `AddDbContext`, whose options are built per scope. Turn on `ValidateScopes` and `ValidateOnBuild` (`dotnet-core`). Refuse the write when there's no `sub`, rather than recording `"system"`.

### High
2. **A07** `Program.cs:29-33`
   - **What:** Keycloak roles aren't mapped, as in row 1. There's also no `ClockSkew` (so it's 5 minutes) and `MapInboundClaims` isn't set.
   - **Fix:** use `AddKeycloakJwt` from `auth-patterns` §1. That means `MapInboundClaims = false`, `RoleClaimType = "roles"`, an `OnTokenValidated` that copies `realm_access` and `resource_access["lab-results"]` roles into role claims, `ClockSkew = 30s` and `RequireHttpsMetadata = true`. Keep the audience as `lab-results`.
3. **A09, PII in logs** `LabClient.cs:11` with `Program.cs:21` and `appsettings.json:15-17`
   - **What:** the national ID is in the path of the outgoing URL. `IHttpClientFactory`'s default logging writes that URL at Information, and `System.Net.Http` isn't filtered.
   - **Impact:** every lookup writes the patient's national ID to the logs in clear.
   - **Fix:** add `.RemoveAllLoggers()` to the `LabClient` and `SmsClient` builders and log your own line with only the status code (`pii-masking`, "HttpClient logs a failed request").
4. **A01/A05, path tampering on the lab API** `ResultEndpoints.cs:63-64`, `LabClient.cs:11`
   - **What:** `nationalId` is never validated, and `Uri.EscapeDataString` leaves `.` alone. `".."` therefore becomes `patients/../orders`, which resolves to `{base}/orders` on the lab host. A null value is a 500.
   - **Fix:** validate before the handler. Convert Arabic-Indic digits to Western ones, require `^[12]\d{9}$`, and return a `ValidationProblem` otherwise (`api-design` §3).
5. **A10, one SMS error stops the service** `NotificationQueue.cs:30`
   - **What:** only `HttpRequestException` is caught. A timeout (`TaskCanceledException`) or any other error escapes `ExecuteAsync`, and .NET's default behaviour (`StopHost`) then shuts the whole service down.
   - **Fix:** catch `Exception` (except cancellation of `stoppingToken`) and log the exception type only. Set a timeout on the client (`polly-resilience`).
6. **A01/A06, SMS to any number with health data in it** `ResultEndpoints.cs:43`
   - **What:** `phone` comes from the request and isn't validated or matched to the patient. The message includes the test name.
   - **Impact:** a result's test name (PHI) goes to any number the caller types, and any clinician token can be used for SMS pumping.
   - **Fix:** validate the mobile format, or better, take the number from the patient's record. Leave the test name out of the text. Rate-limit per caller.

### Medium
7. **A09** `ResultEndpoints.cs:41`, `Masking.cs:9-11`: the log line includes the national ID. `[0-9]{10}` misses Arabic-Indic digits and IDs with spaces, so those go out in clear (`pii-masking`, "Arabic-Indic digits"). Log `PatientId` instead.
8. **A05/A10** `ResultEndpoints.cs:33-41,54`
   - `NationalId`, `Phone`, `Notes` and `Value` aren't validated.
   - `Masking.NationalId(null)` throws *after* `SaveChangesAsync`, so the client gets a 500, the result is already saved, and no SMS goes out.
   - A `Value` over `numeric(10,3)` is a 500.
   - `Notes` has no length limit and no `HasMaxLength` (`LabDbContext.cs:12-22`), so it's unbounded (`efcore-rules`).
   - A bad `before` makes `DateTimeOffset.Parse` throw: a 500 where it should be a 400. Use `TryParse`.
   - Fix: a request validator for each DTO (`api-design` §3).
9. **A02/A10** `Program.cs:39-44`: there's no `AddProblemDetails` or `UseExceptionHandler`, no security headers set in `OnStarting`, and no HSTS or ForwardedHeaders (`api-design` §2, `owasp-aspnetcore` §1-2).
10. **A01/A09** `ResultEndpoints.cs:48-60,63`
    - Any clinician can list every patient's results and look up any national ID.
    - Reads aren't recorded anywhere, and there's no per-caller rate limit on the lookup (`api-design` §6).
    - Keep the route's shape, but add an audit event per read. Decide with the owners whether care-team scoping is needed (`auth-patterns` §4).
11. **Reliability, data integrity** `ResultEndpoints.cs:40-43`, `NotificationQueue.cs:10`
    - Notifications sit in an in-memory queue, so they're lost when the pod restarts.
    - When the queue is full, `EnqueueAsync` blocks the POST after the save, and a retry then creates a duplicate result.
    - Use an outbox (`efcore-patterns` §5).
12. **Paging** `ResultEndpoints.cs:55,59`: the cursor is `ReceivedAt` alone, so results with the same timestamp are skipped at page edges. Use `(ReceivedAt, Id)` encoded in `next`.
13. **A04** `appsettings.json:3`: no `SSL Mode`, so Npgsql defaults to `Prefer`, which can fall back to an unencrypted connection. Set `SSL Mode=VerifyFull`.
14. **A03** `LabResults.csproj`: there's no `NuGetAudit` with `NuGetAuditMode=all`, and `NU1901-1904` aren't treated as errors (`zero-vulns`). I couldn't run the vulnerable-package scan, so this gate is still open.

### Low
15. `Program.cs:21-22,27`: the base addresses and authority aren't checked at startup (they use `!`). Use validated options with `ValidateOnStart`.

**Not checked:** the build, tests, the vulnerable-package scan, how the service behaves with real Keycloak tokens, and the DBA side beyond the column rules above. I found no text in the repo trying to steer the review: `rv-security-out.json` is empty and the journal only repeats the request.
