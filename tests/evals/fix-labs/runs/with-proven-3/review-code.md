## Code Review: LabResults (before production)

### Summary
Not ready for production. Four bugs block release. Clinicians are refused on every route. Results get stamped with the wrong clinician. One SMS timeout stops the whole host. National IDs are written in clear to the logs. I'm a read-only reviewer, so `task.md`'s "edit in place" isn't done: everything below is a proposed fix, and none of it is applied.

**Scan rows:** both confirmed. Row 1 (`Program.cs:32`, Keycloak roles) is really **critical**, not a warning. Row 2 (`Program.cs:9`, scope validation) is right, and it is hiding critical #2.

### Critical (must fix)
1. **[Auth]** `Program.cs:32`. `RoleClaimType = "realm_access.roles"` names a claim that doesn't exist. Keycloak puts the roles inside the `realm_access` JSON claim, so `RequireRole("clinician")` (`:37`) fails and every clinician gets 403 on all three routes. **Fix:** set `RoleClaimType = "roles"` and copy the roles out of `realm_access`/`resource_access` in `OnTokenValidated` (`auth-patterns` §1). Also add `ClockSkew = 30s` (`rules/security.md`).
2. **[DI / audit trail]** `Program.cs:14-17`. `AddDbContextPool` builds its options once, from the root provider, so the scoped `AuditInterceptor`→`CurrentUser` becomes a captive singleton. `CurrentUser.Id` (`CurrentUser.cs:8`) is read when it is built. That means every later result records the first caller's `sub` (or `"system"`), and the hidden `recordedBy` test fails. Production hides this because `ValidateScopes` is off (scan row 2; in Development it would throw). **Fix:** have the endpoint pass the caller's `sub` into `LabResult.Record(..., recordedBy)` and drop the interceptor. Or use `AddPooledDbContextFactory` and set the caller on each rental (`efcore-patterns` §1). Then turn on `ValidateScopes` and `ValidateOnBuild` in every environment (`dotnet-core`).
3. **[Worker stops the host]** `NotificationQueue.cs:30`. The worker only catches `HttpRequestException`. A `TaskCanceledException` from the HttpClient's 100 s timeout, or any other exception, escapes `ExecuteAsync`, and the default `BackgroundServiceExceptionBehavior.StopHost` then stops the whole API. **Fix:** `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)`. Also add the drain-on-shutdown `StopAsync` and `FullMode = Wait` (`worker-patterns`, "An in-process queue that drains on shutdown").
4. **[PII in logs]** `LabClient.cs:11` and `appsettings.json:16`. The national ID is in the URL path. `IHttpClientFactory`'s logging writes the full URI at Information, and only `Microsoft.AspNetCore` is raised to Warning, so every lookup logs the ID in clear. Failures also log the exception text (`pii-masking`, the HttpClient bullet). **Fix:** `.RemoveAllLoggers()` on the `LabClient` builder and log your own line with the status code only. Do the same for `SmsClient`, which handles phone numbers.
5. **[PII masking]** `Masking.cs:9` only masks exactly 10 digits in a row. A 9-digit ID, or one with spaces or dashes, is logged raw. An 11-digit ID leaks its last digit. `ResultEndpoints.cs:41` logs this value, and the ID isn't stored anywhere. **Fix:** log `PatientId` instead. If you keep masking, mask by length (`'*' × (n−4)` + last 4) whatever the shape.
6. **[DBA column rule]** `LabDbContext.cs:12-22`. `Notes` has no `HasMaxLength`, so it becomes an unbounded `text` column (hard ban, `rules/efcore-rules.md`). The endpoint doesn't check its length either. **Fix:** `HasMaxLength(n)` (≤ 4000), and validate it in `RecordAsync`.
7. **[Input validation / outbound path]** `ResultEndpoints.cs:63-64`. `NationalId` is never validated. With `null`, `EscapeDataString` throws and the caller gets a 500. With `..`, `EscapeDataString` leaves the dots alone, so `patients/../orders` resolves to another path on the lab API. `NationalId` and `Phone` in `RecordAsync` (`:33`) aren't checked either, so any caller can send SMS to any number (SMS pumping). **Fix:** a validator per request DTO: national ID `^[12]\d{9}$`, phone in E.164 `+9665…` form, `Value` within `numeric(10,3)`, `PatientId > 0`.

### Warnings (should fix)
1. **[Pagination]** `ResultEndpoints.cs:55,59`. The cursor uses `ReceivedAt` alone with a strict `<`, so results with the same timestamp at a page boundary are skipped. `DateTimeOffset.Parse` (`:54`) throws on bad input, which gives a 500. A cursor with a non-zero offset makes Npgsql throw on `timestamptz`. **Fix:** a `(ReceivedAt, Id)` keyset cursor with a matching composite index, `TryParse` → 400, and `.ToUniversalTime()`. The `before`/`next` contract stays the same.
2. **[Reliability]** `ResultEndpoints.cs:40-43`. The row is saved, then the SMS is queued in memory with the request's token. A cancel, a full queue or a pod restart loses the SMS, and a client retry stores a duplicate result. **Fix:** an outbox row in the same `SaveChanges` (`outbox`).
3. **[Resilience]** `Program.cs:21-22`. No timeout or retry policy on either client. `!` on config values that may be missing means a bad deploy only fails at first use. **Fix:** `AddStandardResilienceHandler()` (`polly-resilience`), and bound options with `ValidateOnStart()` (`dotnet-core`).
4. **[Errors]** `Program.cs:39-44`. No `AddProblemDetails`/`UseExceptionHandler`. An upstream 404 from the lab (`GetFromJsonAsync` throws) comes back as a bare 500. **Fix:** a global handler, and map the lab's 404 to an empty list or a 404.
5. **[PHI over SMS]** `ResultEndpoints.cs:43`. The SMS names the test (e.g. HbA1c) on a channel others can read. **Fix:** a generic "your result is ready" text, kept in a resource rather than a literal (`healthcare-compliance`).
6. **[Access scope]** `ResultEndpoints.cs:48-61`. Any clinician can list every patient's results, and lookups aren't audited. This is a product decision, so confirm it. Filtering by caller would change the `GET /results` contract the hidden tests use.
7. **[Layers]** `NotificationQueue.cs:2` (Application references Infrastructure's `SmsClient`). The endpoints use `LabDbContext` directly, and the API returns Infrastructure's `LabOrder` (`LabClient.cs:5`) (`rules/architecture.md`).

### Suggestions
- `ResultEndpoints.cs:31`: `ILogger<RecordResultRequest>` uses a DTO as the log category. Use `ILoggerFactory` with a named category.
- `ResultEndpoints.cs:44`: the `Location` header points to `/results/{id}`, a route that doesn't exist.
- `LabResults.csproj`: no `NuGetAudit`/`NU1901-4` as errors, and no `TreatWarningsAsErrors` for the nullable warnings (`zero-vulns`).

### Positive
- The fallback policy denies by default, and only `/health/live` is anonymous.
- The app never runs migrations.
- `TimeProvider` comes from DI, and `InvariantCulture` is used for the cursor.
- The other strings are bounded, `numeric`/`timestamptz` are explicit, the list query uses `AsNoTracking`, the cancellation token is passed all the way down, and the classes are sealed.

**Not checked:** I didn't build anything or run vulnerability checks on the packages. Roslyn wasn't used: the service is 11 files and I read all of them.
