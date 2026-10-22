## Code Review: HomeReadings (/tmp/claude-0/labruns/with-proven-3/HomeReadings), the code-reviewer's part

### Summary
Not ready for production. Two faults can each take the whole pod down: any alert failure, and the culture setup on a slim image. The care-team alert path can page nurses twice, or never for the highest readings. Several faults leak PHI across patients or into logs. Paths below are relative to the service folder.

**Known-traps item `Program.cs:7` (scopes not validated): CONFIRMED. Keep it as info.** `CreateBuilder` turns on ValidateScopes and ValidateOnBuild only in Development. I checked every singleton and none has a captive dependency today: `ReadingNotifier` takes `IServiceScopeFactory`/`TimeProvider`/`ILogger`, and `PhotoStore` takes only `IConfiguration`. But the tests also run in Production, so no environment ever checks lifetimes. Fix: `ValidateServicesInEveryEnvironment()` (`dotnet-core`, Rules).

### Critical (must fix)
1. **Worker kills the host** `Application/ReadingNotifier.cs:33-36` (and 18, 19). Nothing catches errors inside `RunOnceAsync`. Things that can throw there: a care-team 5xx after retries, an open circuit, a DB blip, or an orphan alert making `SingleAsync` throw. With the default `StopHost`, the whole API pod stops. The same failing alert then fails again on every restart, so the pod crash-loops. **Fix:** catch per run with `when (ex is not OperationCanceledException)`, as `PeriodicSyncWorker` does. Also catch per alert and keep an attempt count so one bad alert can't block the rest. (`worker-patterns`)
2. **Nurses get duplicate alerts (care team has no idempotency key)**:
   - (a) `Program.cs:20-21`: `AddStandardResilienceHandler()` retries POST, and a 10 s attempt timeout can fire after the care team already accepted the alert. Add `o.Retry.DisableForUnsafeHttpMethods()` (works on 10.0.0). (`polly-resilience`)
   - (b) `ReadingNotifier.cs:19-21`: the alert is sent, then marked. If the pod crashes, or the save fails or is cancelled by `stoppingToken` on shutdown, the alert is sent again. Save the "sent" mark with `CancellationToken.None`.
   - (c) Every replica runs the loop and reads the same 50 rows (line 15). Claim the rows with `FOR UPDATE SKIP LOCKED` (the `WorkClaims` pattern), or run one relay under a lock.
   - Decide at-most-once vs at-least-once on purpose. Claim and commit, then send. After an ambiguous timeout, flag the alert for follow-up rather than resending it. Ask the care team to accept the alert Id as a key. (`worker-patterns`, `outbox`)
3. **"HI"/"LO" or a decimal comma loses the reading, and "HI" is the most urgent one** `Application/ReadingService.cs:14`. `decimal.Parse(request.Value)` uses the current culture, which is ar-SA for Arabic users. The value comes "as the meter shows it": `"12.5"` can fail under ar-SA's `٫` separator, and a meter's `HI`/`LO` text always throws. The result is a 500, and above-range glucose never reaches the care team. **Fix:** `TryParse(..., NumberStyles.Number, CultureInfo.InvariantCulture)`. Map HI/LO explicitly, with HI raising an alert. Anything else gets a 400 ValidationProblem. (`localization` §1/§13, rule csharp-standards InvariantCulture)
4. **ar-SA becomes the formatting culture** `Api/LanguageMiddleware.cs:8-9,14`. It causes item 3. On an image without ICU, `new CultureInfo("ar-SA")` (and `en-US`) throws in the static initializer, so every request returns 500. **Fix:** set the UI culture only, or carry the language code (`LanguageHeader.Parse`) and leave `CurrentCulture` alone. `Messages.cs` only needs the language. (`localization` §1-3, §13)
5. **Alert threshold ignores the unit** `Domain/Reading.cs:34` with free-text `Unit`. A mg/dL meter (common in KSA) sends 180, and 180 ≥ 11.1, so almost every reading pages a nurse. A mmol/L check run on mg/dL values is clinically wrong. **Fix:** validate the unit against an allowlist and convert to mmol/L before `NeedsAlert`.
6. **Reading and alert are saved in two steps** `ReadingService.cs:16-23`. If the second `SaveChanges` fails, a high reading is stored with no alert, ever. The client also gets a 500 and retries, which creates a duplicate reading. **Fix:** one `SaveChanges`: give `CareTeamAlert` a `Reading` navigation, and EF inserts both in one transaction. (rules efcore/architecture)
7. **National ID comes from the request body** `ReadingService.cs:14`, `NewReading` line 7. A patient's token can file a reading under someone else's national ID, and the care team is alerted about the wrong person. **Fix:** derive it from the authenticated patient (claim or patient record), never from the body.
8. **IDOR** `Application/ReadingQueries.cs:12-16`. `GetAsync` doesn't filter by `patient.Id`, so any patient can read any reading, Notes included. **Fix:** add `&& r.PatientId == patient.Id`, so someone else's record returns NotFound. (`api-design` §11, rule security.md)
9. **One cache entry shared by all patients** `ReadingQueries.cs:32`. Key `"readings:latest"` serves patient A's latest reading to everyone for one minute. **Fix:** `$"readings:v1:latest:{patient.Id}"` (InvariantCulture). (`caching`)
10. **PHI in logs** `ReadingService.cs:25`. `{@Reading}` sends NationalId, Notes and Value to the log sink. **Fix:** log the Id only, or register `SensitiveFieldDestructuringPolicy`. (`pii-masking`)
11. **Exception text sent to the client** `Api/ErrorMiddleware.cs:16`. `ex.Message` and the inner message expose Npgsql/constraint details. The middleware also logs client aborts as errors and writes after the response has started. **Fix:** `AddProblemDetails()` + `UseExceptionHandler()` with no exception text. (`api-design` §11)
12. **Path traversal in photo upload** `Infrastructure/PhotoStore.cs:12`. `Path.Combine(folder, file.FileName)`: a name like `../../x`, or a rooted `/app/...`, writes anywhere the process can. There is no size or type check. **Fix:** a server-generated name, an extension/content-type allowlist and a size cap. A local volume is also not shared across pods, so use object storage with a bounded key. (rule security.md file uploads, efcore-rules)
13. **Unbounded column (DBA blocking rule)** `Infrastructure/ReadingsDbContext.cs:13-24`. `Notes` has no `HasMaxLength`, so it becomes `text`. **Fix:** `HasMaxLength(n)` ≤ 4000 and a matching request validator. (rules/efcore-rules.md)

### Warnings (should fix)
1. `ReadingQueries.cs:21-22`: "today" is the UTC day. Riyadh patients see yesterday's chart until 03:00. Use the Riyadh day via `RiyadhTime` (fixed-offset fallback). (`localization` §7)
2. `NewReading.CollectedAt` is a `DateTime` (`ReadingService.cs:7`). Npgsql rejects a Kind other than Utc for `timestamptz`, so if the app sends local time without `Z` every POST returns 500. Take `DateTimeOffset` and convert to UTC. (`postgresql-patterns` §2)
3. `NewReading` has no validator (lengths, unit, required fields). An over-long NationalId becomes a DbUpdateException 500. (rules/security.md)
4. `ReadingNotifier.cs:18`: N+1 query per alert. `ReadingsDbContext.cs:25-32`: no FK from alert to reading. Join in one query and add `HasOne<Reading>().WithMany().HasForeignKey(a => a.ReadingId)`.
5. `Program.cs:31,40`: the DB check also runs on `/health/live`, so a DB outage restarts every pod. Liveness should check nothing external. (`kubernetes-dotnet`)
6. `Api/CurrentPatient.cs:12`: `int.Parse` + `!` on NameIdentifier. A missing or non-int `sub` gives a 500, not a 401. The Authority looks like a Keycloak realm, where `sub` is a UUID, so check which claim carries the int id.
7. Layering: Application depends on `Api.ICurrentPatient` and on `ReadingsDbContext` directly. Move `ICurrentPatient` into Application. (rules/architecture.md)
8. Photo upload goes through nginx/ingress (default 1 MB) and Kestrel (30 MB) with no agreed limit, so phone photos will get 413. Set all three limits to the same value. (`nginx`)
9. `appsettings.json:3`: a DB password is committed in source, even though it's a placeholder. (`secret-management`)

### Suggestions
1. Known trap above: turn on ValidateScopes and ValidateOnBuild everywhere. (`dotnet-core`)
2. The `!` on `CareTeam:BaseAddress` (`Program.cs:20`) and on `PhotoRoot` (`PhotoStore.cs:6`): bind options with `ValidateOnStart()` so missing config fails at start. (`dotnet-core`)
3. JWT: set `ClockSkew = 30s`, and add a deny-by-default fallback authorization policy. (rules/security.md)
4. The notifier waits one minute before its first run. Use a `do/while` so it runs at start. Use a partial index on `sent_at IS NULL`.
5. Add an idempotency key on `POST /readings`, because mobile clients retry.

### Positive
`TimeProvider` everywhere, including `PeriodicTimer`. `AsNoTracking` projections. `int` identity-always keys and `timestamptz`. Base address ends in `/`. Typed `HttpClient`. CancellationTokens are passed through. The notifier is registered once as a singleton and as the hosted service, so tests can call `RunOnceAsync`. `AttachPhotoAsync` checks ownership and returns NotFound. Classes are sealed. No `Migrate()` call.

### Not checked
- There are no test files under the service folder, so there were no tests to review against `testing-tdd`.
- The Roslyn MCP wasn't available, so I mapped who uses what with Read and Grep (16 files, small enough to read fully).
- Security and DBA depth is left to the parallel reviewers.