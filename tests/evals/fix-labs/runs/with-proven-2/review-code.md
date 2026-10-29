I've read all 13 files, `task.md`, `scan.md` and the relevant skills. Here is the review.

## Code review: LabResults

### Summary
The service is small and mostly well built. It has three bugs that break it in Production: clinicians get 403 on every endpoint, every result is stamped with the wrong clinician, and one SMS timeout stops the host. It also leaks PHI in several places. I made no edits, built nothing and ran nothing. Roslyn wasn't available, so I read all 13 files directly.

### Scan rows
- **`Program.cs:32` RoleClaimType: confirmed, and it's critical, not a warning.** No claim called `realm_access.roles` exists, so `RequireRole("clinician")` never passes. Every request to `/results` and `/lab-orders/lookup` gets 403. **Fix:** map the roles out of the `realm_access` JSON claim into `ClaimTypes.Role` in `OnTokenValidated` or an `IClaimsTransformation` (`auth-patterns` §Keycloak, lines 62-73 and 118).
- **`Program.cs:9` ValidateScopes: confirmed, and it hides critical #2.** Set `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })` (`dotnet-core` lines 18-41).

### Critical (must fix)
1. **Audit / DI** `Program.cs:14-17`, `AuditInterceptor.cs:8`, `CurrentUser.cs:8`: `AddDbContextPool` builds its options once, from the root provider. So the scoped `AuditInterceptor` and its `CurrentUser` are captured once. `Id` is read in the constructor, so the first caller's `sub` (or `"system"`) goes into `recordedBy` on every result from then on. That breaks the `recordedBy` contract and the audit trail. Production doesn't validate scopes, so nothing warns. **Fix:** use `AddPooledDbContextFactory` and set the caller on each rental, as in `efcore-patterns` §1. Or use `AddDbContext` with a scoped interceptor. Simplest of all: pass the `sub` into `LabResult.Record`. Also override the sync `SavingChanges`.
2. **Worker crash** `NotificationQueue.cs:30`: only `HttpRequestException` is caught. An `HttpClient` timeout throws `TaskCanceledException`, which escapes the loop. With `StopHost` (the default), the whole API stops. **Fix:** `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)` (`worker-patterns` lines 61 and 278).
3. **DBA column rule (blocking)** `LabDbContext.cs:12-22`: `Notes` has no `HasMaxLength`, so it becomes an unbounded `text` column. **Fix:** `HasMaxLength(n)` (n ≤ 4000), plus a matching check in the request validation (`rules/efcore-rules.md`).
4. **PHI in logs** `Program.cs:21`, `LabClient.cs:11`: the national ID is in the URL path. `IHttpClientFactory` logs the full URI at Information, and only `Microsoft.AspNetCore` is raised to Warning (`appsettings.json:17`). So every lookup writes a plain-text national ID to the logs, and failures write it again in the exception text. **Fix:** call `.RemoveAllLoggers()` on the `LabClient` builder and log your own line with the status code (`pii-masking` line 105; `healthcare-compliance` line 218). Also ask the lab whether they accept the ID in a POST body.
5. **Path traversal** `LabClient.cs:11`, `ResultEndpoints.cs:63`: `nationalId` is never validated. `Uri.EscapeDataString` leaves `.` alone, so `".."` turns the URL into `{base}/patients/../orders`, which resolves to `{base}/orders`. A caller can reach other lab endpoints with the service's credentials. **Fix:** require exactly 10 digits and return 400 otherwise.
6. **Lost SMS** `ResultEndpoints.cs:40-43`, `NotificationQueue.cs:10`: the result is saved first, then the SMS is queued in memory. A pod restart or crash loses queued SMS, and a cancelled request after the save loses that one too. Once 1000 are queued, `WriteAsync` blocks the POST request. **Fix:** write an outbox row in the same `SaveChanges` and have a relay send it (`outbox` §1).

### Warnings (should fix)
1. **Data exposure** `ResultEndpoints.cs:48-58`: `GET /results` returns every patient's results to any clinician. **Fix:** filter by caller (`RecordedBy == sub`) or by a care relationship (`api-design` §11; `rules/security.md` on ownership).
2. **Paging** `ResultEndpoints.cs:55-59`: the cursor uses `ReceivedAt` alone with a strict `<`, so rows with the same timestamp at a page boundary are skipped. The `"O"` format contains `+00:00`, and an unencoded `+` comes back as a space. **Fix:** make the cursor `(ReceivedAt, Id)`, return it as an opaque URL-safe token, and add an index on `(ReceivedAt DESC, Id DESC)`.
3. **500 on bad input** `ResultEndpoints.cs:54`: `DateTimeOffset.Parse` throws on bad input. Npgsql also rejects a non-UTC `DateTimeOffset`, so `before=...+03:00` fails too. **Fix:** `TryParse` with `AdjustToUniversal`, and return 400 when it fails.
4. **Errors** `Program.cs:39-42`: there is no `AddProblemDetails`/`UseExceptionHandler`. When the lab is down or returns 404, `GetFromJsonAsync` throws and the caller gets a bare 500. **Fix:** add both. In `LabClient`, map a 404 to an empty list and other failures to a 502/503 ProblemDetails (`api-design`).
5. **Resilience** `Program.cs:21-22`: no timeouts, so the default 100 s applies, and no retry policy. **Fix:** `AddStandardResilienceHandler()` with explicit timeouts (`polly-resilience`).
6. **Input validation** `ResultEndpoints.cs:33`: `Phone`, `NationalId`, `PatientId`, `Notes` and the `Value` range are never checked. An unchecked phone lets any clinician send SMS to any number (toll fraud). Values above `numeric(10,3)` overflow in the database and return 500. **Fix:** a FluentValidation validator, with E.164/`+9665` for the phone.
7. **PHI over SMS** `ResultEndpoints.cs:43`: the SMS includes the test name (e.g. HbA1c), which is health data sent to an unverified number. **Fix:** use generic text. The hard-coded Arabic string should also move to resources (`localization` §13).
8. **Masking** `Masking.cs:9`: if the value isn't exactly 10 digits, it is logged as-is. An 11-digit value leaks a digit. **Fix:** log `PatientId` instead of the national ID (`pii-masking`).
9. **Config** `Program.cs:21-22, 27`: the `!` hides a missing setting until the first call. **Fix:** bind options with `ValidateOnStart()`.
10. **JWT** `Program.cs:29-33`: `ClockSkew` keeps its 5-minute default. **Fix:** set it to 30 s (`rules/security.md`).
11. **Layering** `NotificationQueue.cs:2,28`: Application references `Infrastructure.SmsClient`. **Fix:** add an `ISmsSender` interface in Application.

### Suggestions
- `ResultEndpoints.cs:44`: the `Location` header points to `/results/{id}`, a route that doesn't exist.
- `ResultEndpoints.cs:31`: the logger category is `ILogger<RecordResultRequest>`, a DTO.
- `ResultEndpoints.cs:63`: the infrastructure type `LabOrder` is exposed in the API.
- `Program.cs`: no `UseForwardedHeaders`/HSTS if the service runs behind nginx (`nginx` §7). No audit row for who looked up whose national ID (`healthcare-compliance`).

### Positive
- The fallback policy requires an authenticated user, so endpoints are denied by default.
- `TimeProvider` comes from DI, `AsNoTracking` is used on reads, and `CancellationToken` is passed through.
- Classes are sealed, entity setters are private, `Test`/`Unit`/`RecordedBy` are bounded, and the `int` key is right.
- I found no text in the repo addressed to an AI reviewer.

**Not checked:** I didn't build or run anything, so the NuGet vulnerability audit wasn't run. I didn't check how the hidden tests sign their tokens (it matters for the roles fix).
