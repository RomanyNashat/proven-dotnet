## Code Review: MeterSync (/tmp/claude-0/syncruns/with-proven-2/MeterSync)

### Summary
Not ready for production. All 6 scan rows hold up. On top of those I found three patient-safety bugs (mmol/L readings are never converted, HI/LO readings are dropped, alerts go out for a meter's whole history), duplicate SMS when two pods run, and one bad meter or phone number that stops sync or alerts for every patient. The column rules and the absence of a migration runner are clean. Every fix below keeps the routes, shapes and settings that task.md protects.

### Scan rows
1. `Program.cs:13` singleton `PatientDirectory` takes the DbContext: **confirmed, critical.** Every `GET /meters` shares one context, so two at once throw "a second operation was started" and return 500. Fix: `AddScoped<PatientDirectory>()` (dotnet-core "The host").
2. `Program.cs:39-40` `SetIsOriginAllowed(_ => true)` with `AllowCredentials`: **confirmed, critical.** Any site gets its own origin echoed back. Fix: `.WithOrigins(config.GetSection("Cors:Origins").Get<string[]>())` and drop `AllowCredentials`, since this is a bearer API (owasp-aspnetcore lines 89-111).
3. `AlertText.cs:13` time-zone lookup with no fallback: **confirmed, and worse than the row says.** `AlertText.cs:9/14` formats with plain `ar-SA`. On one ICU the SMS date comes out Hijri (`10/05/1448`), on another Gregorian, and with no ICU the static field throws `CultureNotFoundException` (so every send fails with `TypeInitializationException`). Fix: a static `RiyadhTime.Zone` with a fixed UTC+3 fallback (localization §7), and either a Gregorian clone of `ar-SA` (§6) or `yyyy-MM-dd HH:mm` with `InvariantCulture` if the image has no ICU (§1).
4. `Program.cs:30` `ValidateAudience = false`: **confirmed, raised to critical.** Any token from the patient realm is accepted, health data included. Fix: `o.Audience = "meter-sync"`, `ValidateAudience = true` (auth-patterns §1, "Audience").
5. `appsettings.json:6` base address with no trailing slash: **confirmed, raised to critical.** Every vendor call goes to `/meters/...` without the `/v2`, gets a 404, `GetFromJsonAsync` throws, and no reading ever syncs. Production's value is fixed (task.md), so a start-up check that rejects it would block the deploy. Fix: make the address end in `/` at registration, at `Program.cs:19` and `:22` (dotnet-core "HTTP clients").
6. `Program.cs:9` scopes aren't validated in every environment: **confirmed.** Fix: `.ValidateServicesInEveryEnvironment()` (`ValidateScopes` + `ValidateOnBuild`). It is safe once row 1 is fixed: `AlertSweeper` and `SyncWorker` already use `IServiceScopeFactory`.

### Critical (beyond the scan)
1. **Correctness** `MeterSyncService.cs:21-27`, `Reading.cs:41-50`: the unit is never converted, though VENDOR.md says it can be mmol/L. A 20 mmol/L reading (360 mg/dL) is saved as 20 "mg/dL" and the patient is told their sugar is low. A normal 5.5 mmol/L reading gets a false "low" alert. **Fix:** let `Reading.FromMeter` take the raw value and unit, multiply mmol/L by 18, and reject any other unit.
2. **Correctness** `MeterSyncService.cs:21-25`: `HI` (over 600) and `LO` (under 20) fail `TryParse` and are skipped. These are the most dangerous readings, and no alert goes out. They also come back from the vendor every minute and log the same warning each time. **Fix:** map `HI` to a High alert and `LO` to a Low alert (store 600 or 20, keep `Raw`).
3. **Multi-pod** `AlertSweeper.cs:16-30`: both pods select the same unsent readings and both send. The gateway has no idempotency key (VENDOR.md), so the patient gets every SMS twice. **Fix:** claim rows before sending: `UPDATE ... WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING`, commit the claim, then send (worker-patterns "Claiming a batch"). The other option is a PostgreSQL advisory lock so only one sweeper runs (outbox skill). Sending only after the claim is committed is at-most-once: a crash leaves a claimed row unsent. Decide whether a stale claim gets retried.
4. **Resilience** `AlertSweeper.cs:28`: one failed send stops every alert queued behind it. A gateway 4xx for one phone number throws out of the loop, and that reading stays first in `OrderBy(Id)`, so every run stops at the same row. `phones[...]` throws `KeyNotFoundException` the same way for a reading whose patient is gone (there is no foreign key, see W4). **Fix:** try/catch per reading, count attempts, and mark a reading failed after N tries.
5. **Resilience** `MeterSyncService.cs:17,32`: one bad meter stops sync for everyone. A 404 for a mistyped serial, or one value too long for its column (`Raw` max 16, `VendorId` max 64), fails the run, or the single `SaveChanges`, every minute. Two pods inserting the same `VendorId` hit the unique index and roll back a whole batch. `SyncWorker.cs:14-19` then skips the sweeper for that tick too. **Fix:** fetch and save per meter inside its own try/catch, insert with `ON CONFLICT (vendor_id) DO NOTHING` or catch error 23505, and give the sync and the sweeper separate try blocks.
6. **Correctness** `AlertSweeper.cs:17`: the vendor returns a meter's whole history, so linking a meter texts the patient about every past out-of-range reading, 100 per minute. **Fix:** only alert on readings taken after the meter was linked (or within a recent window). Mark the older ones as not needing an SMS.
7. **Security, record ownership** `MeterEndpoints.cs:35-46`: any signed-in caller can link a meter to any national ID. Foreign readings and SMS alerts then land in someone else's record. The 404/409/201 answers also tell an attacker which national IDs exist. **Fix:** keep the body as it is, but return NotFound unless `patient.Id == CurrentPatient.Id(user)`, or require a clinic-staff policy if the clinic is meant to call it (api-design §11, auth-patterns §4). Ask which caller is intended.

### Warnings
1. `MeterEndpoints.cs:41-48`: two requests with the same serial race. Both pass `AnyAsync`, the unique index rejects one, and it returns 500. Fix: catch 23505 and return `Conflict`.
2. `PatientDirectory.cs:1,12`: Infrastructure uses `Api.MeterResponse`, so the layers point the wrong way. Fix: return an Application read model, or move the query into Application.
3. `MeterSyncService.cs:18`: one query per meter every minute that loads all of that meter's vendor IDs, and nothing indexes `readings.meter_id`. Fix: `Where(r => incomingIds.Contains(r.VendorId))`, which the unique index serves.
4. `SyncDbContext.cs:16-45`: no foreign keys between readings, meters and patients. Fix: configure the relationships (they also create the `meter_id` index).
5. `Program.cs:19,22,27`: settings are read with `!` and no options validation. A missing setting fails on the first call, not at start-up. Fix: options with `ValidateOnStart`, but no trailing-slash rule, see row 5 (dotnet-core "Options").
6. `MeterSyncService.cs:23`: logs a glucose value with its reading ID, which is health data in the logs (rules/security.md, Data Protection).
7. `CurrentPatient.cs:9`: `int.Parse` with `!` returns 500 on a missing or non-numeric `sub`. Fix: `MapInboundClaims = false`, read `sub`, `TryParse`, and return 401 on failure (auth-patterns §5).

### Suggestions
- `AlertText.cs:16`: the Arabic text runs past 70 characters, so every alert is a multi-part SMS (localization §10). It also has no wording for HI/LO.
- `SyncDbContext.cs:44`: a partial index `WHERE alert <> 'None' AND alert_sent_at IS NULL` would suit the sweeper's query better.
- Add `AddProblemDetails` and an exception handler.

### Positive
- No `Migrate()` or `EnsureCreated()`, every string is bounded, keys are `int`, timestamps are `timestamptz`, and the value is `numeric(6,1)`.
- `TimeProvider` is used throughout, including `PeriodicTimer(…, time)`, and the catch skips `OperationCanceledException`.
- `AlertSweeper` uses `IServiceScopeFactory`. `SmsClient` correctly has no retry handler, since its POST isn't idempotent (polly-resilience line 64). The vendor GET has the standard resilience handler.
- `AsNoTracking` and a projection on the list query, and UTC is stored.

### Not checked
- The proven-roslyn MCP wasn't available to me, so this was read and grep only, over the 17 files.
- Nothing was built or run. There are no tests in the folder.
- nginx and forwarded headers: no ingress config is in the folder.
- No text addressed to an AI reviewer was found in the repo.