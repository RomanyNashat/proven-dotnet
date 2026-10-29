## Security review: LabResults (before production)

The service isn't ready. Two audit and access defects block it, and national IDs reach the logs by two separate routes. I read every file under `LabResults/` plus `task.md` and `scan.md`. All public contracts listed in task.md survive every fix below.

### Scan rows
- **`Program.cs:32` RoleClaimType `realm_access.roles`: confirmed.** That claim doesn't exist. Keycloak puts roles inside the JSON `realm_access` claim, so `RequireRole("clinician")` (`Program.cs:37`) returns 403 to every clinician. It fails closed, but the service is unusable. Fix: `MapInboundClaims = false`, `RoleClaimType = "roles"`, and copy the roles out of `realm_access` in `OnTokenValidated` (auth-patterns §1, §8).
- **`Program.cs:9` scope validation: confirmed, and worse than "info".** It hides Critical #1 below. Add `ValidateScopes = true, ValidateOnBuild = true` for every environment (dotnet-core, Rules). Turning it on makes the current code throw at startup, so fix #1 first.

### Critical
1. **[A01/A09] `Program.cs:14-17`, `CurrentUser.cs:8`: `recordedBy` is wrong on every result after the first.** `AddDbContextPool` builds its options once, from the root provider. So the scoped `AuditInterceptor` and its `CurrentUser` are created once, and `CurrentUser` reads `sub` in its constructor. Every later result is stamped with the first clinician's `sub`, or `"system"`. In Production nothing reports this, because scope validation is off.
   **Impact:** the clinical audit trail is forged, and every result is attributed to the wrong clinician.
   **Fix:** take `ClaimsPrincipal user` in `RecordAsync` and pass `user.FindFirstValue("sub")` into `LabResult.Record(..., recordedBy)`. Return 401 if it's missing, instead of falling back to `"system"`. Alternatively, keep the interceptor but make it a singleton that reads `IHttpContextAccessor` inside `SavingChangesAsync` (efcore-patterns §4, lines 211-214, and checklist line 405).

### High
2. **[A09 PII] `LabClient.cs:11` with `Program.cs:21`: full national IDs logged by HttpClient.** The lab API takes the national ID in the URL path. `IHttpClientFactory` logs the request URI at Information, and `appsettings.json` only quiets `Microsoft.AspNetCore`. .NET redacts query strings in these logs, not paths. Fix: call `.RemoveAllLoggers()` on the `LabClient` builder and log your own line with the status code only (pii-masking, lines 105-110).
3. **[A09 PII] `Masking.cs:9-11`, `ResultEndpoints.cs:41`: the mask fails open.** `[0-9]{10}` misses Arabic-Indic digits (`١٠١٢...`), spaced input and 9- or 11-digit values. Anything it doesn't match is logged whole, and `NationalId` is never validated. Fix: log `PatientId` instead of the national ID. If the ID must be logged, mask by refusal: return `"[redacted]"` unless the value is exactly 10 digits (pii-masking, line 29 and the `SaudiNationalIdMaskingOperator` at line 166).
4. **[A10] `NotificationQueue.cs:30`: one SMS timeout stops the whole service.** The worker catches only `HttpRequestException`. An HttpClient timeout throws `TaskCanceledException`, which escapes `ExecuteAsync`, and .NET 8+'s default `BackgroundServiceExceptionBehavior.StopHost` then stops the host. Fix: `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)`, log `ex.GetType().Name`, and set a short timeout on the `SmsClient`.
5. **[A05/A06] `ResultEndpoints.cs:33,63`: input is barely validated.**
   - `NationalId` is unchecked. A null body value throws a 500 in `EscapeDataString`, and `..` turns the lab URL into `/api/orders`.
   - `Phone` is unchecked, so a clinician can send SMS to any number (SMS pumping).
   - `PatientId > 0` isn't checked.
   - `Value` isn't range-checked; anything that overflows `numeric(10,3)` gives a 500 from the database.
   - `Notes` has no length check.

   Fix: a validator that requires `^[0-9]{10}$` for `NationalId` and `^\+9665[0-9]{8}$` for `Phone`, enforces the value range, and caps `Notes` at a set length.
6. **[A07] `Program.cs:29-33`: `ClockSkew` is left at the 5-minute default.** Set `ClockSkew = TimeSpan.FromSeconds(30)` and `RequireHttpsMetadata = true` explicitly (auth-patterns §8).

### Medium
7. **[A02/A10] `Program.cs:39-44`: no error handling or security headers.** There's no `AddProblemDetails`, `UseExceptionHandler` or `UseStatusCodePages`, so errors come back as bare 500s with no trace id. There's also no HSTS, `nosniff` or `X-Frame-Options`. Set the headers in `OnStarting` so they appear on 500 responses too.
8. **[A10] `ResultEndpoints.cs:54`: a bad cursor crashes the request.** `DateTimeOffset.Parse` throws a 500 on bad input. A cursor with a non-zero offset also makes Npgsql throw (`timestamptz` only accepts offset 0). Fix: `TryParse`, return 400 on failure, then `.ToUniversalTime()`.
9. **[Integrity] `ResultEndpoints.cs:55,58`: paging can skip results.** The cursor uses only `ReceivedAt`, and `<` drops any result that shares the boundary timestamp. A clinician silently never sees it. Fix: order by `(ReceivedAt desc, Id desc)` and encode both values in the opaque `next` string (api-design, keyset paging).
10. **[DB rules] `LabDbContext.cs:12-22`: `Notes` has no `HasMaxLength`.** That makes it an unbounded `text` column, and this is free text that may hold patient data. Add `HasMaxLength(2000)` and validate the same limit.
11. **[PHI] `ResultEndpoints.cs:43`: the SMS names the test.** It goes to a phone number taken from the request and never checked against the patient. Send a generic "your result is ready" message.
12. **[A06] `ResultEndpoints.cs:25`: no rate limit on the lookup.** One token can enumerate national IDs through the lab API. Add a per-`sub` limiter.

### Low / informational
- `NotificationQueue.cs:10`: the queue is in memory, so notifications are lost on restart. When the gateway is down the bounded queue fills, and `RecordAsync` then blocks while waiting to enqueue.
- `NotificationQueue.cs:32`: `LogError(ex, ...)`. HttpClient's exception text holds no phone today, but log the exception type and status code instead.
- `GET /results` returns every patient's results to any clinician. That's the required contract, but the access model (tenant or care team) should be confirmed.
- I found no text in the repo addressed to an AI reviewer.

### Not checked
- `dotnet list package --vulnerable --include-transitive`: I have no shell here. JwtBearer 10.0.0 and Npgsql.EF 10.0.0 are unaudited. Under the zero-vulns standard, run it before merge; any hit blocks.
- I built nothing and ran nothing.
