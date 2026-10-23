## Code Review: MeterSync (/tmp/claude-0/syncruns/with-proven-3/MeterSync)

### Summary
Not ready for production. I confirmed all 6 scan rows, but one needs a different fix. The bigger problems are ones the scan doesn't cover. HI/LO readings and mmol/L readings produce no alert or a wrong one. On two pods every alert SMS goes out twice. Any authenticated patient can link a meter to anyone's national ID. A slow SMS gateway can stop the whole host.

### Scan rows
1. **Confirmed, critical.** `Program.cs:13`: `PatientDirectory` is a singleton that holds the scoped `SyncDbContext`. Every `GET /meters` shares one context, so concurrent calls throw "A second operation was started". Fix: `AddScoped<PatientDirectory>()` (`dotnet-core`, `design-patterns` singleton row).
2. **Confirmed, critical.** `Program.cs:39-40`: `SetIsOriginAllowed(_ => true)` plus `AllowCredentials`. Fix: `WithOrigins(config.GetSection("Cors:Origins").Get<string[]>())` and drop `AllowCredentials`, since the API uses bearer tokens, not cookies (`owasp-aspnetcore` :99/:111/:262).
3. **Confirmed, warning.** `AlertText.cs:13`: bare `FindSystemTimeZoneById`. On an image without tzdata every sweep throws. Fix: `RiyadhTime` with a fixed +3h fallback, resolved once rather than per call (`localization` §7). The same file has a second problem, in finding 13.
4. **Confirmed, warning.** `Program.cs:30`: `ValidateAudience = false`. Fix: `ValidateAudience = true, ValidAudience = "meter-sync"` (`auth-patterns` :51, :347). The tests depend on this audience.
5. **Confirmed, but the fix is in code, not config.** `appsettings.json:6` with `MeterVendorClient.cs:11`: calls go to `https://meters.example.net/meters/...` and lose `/v2`. task.md says production has this exact value without the trailing slash, so editing appsettings changes nothing in production. Fix at `Program.cs:19`: add the `/` when it's missing (or bind validated options that normalise it) (`dotnet-core` :72, :113).
6. **Confirmed, info.** `Program.cs:9`: add `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })` so row 1 fails at startup in every environment (`dotnet-core` :18-41).

### Critical: not in the scan
7. **Patient safety.** `MeterSyncService.cs:21-25`: `HI` and `LO` fail `decimal.TryParse` and are skipped. These are the most dangerous readings (over 600 or under 20 mg/dL, per VENDOR.md:12) and they never alert. They are also re-fetched and logged as a warning every minute, forever. Fix: map `HI` to High and `LO` to Low and store them with `Raw` kept.
8. **Patient safety.** `MeterSyncService.cs:27` with `Reading.cs:50`: `unit` is ignored. A mmol/L value is stored as `ValueMgDl` and compared with mg/dL limits, so 5.5 mmol/L (99 mg/dL) gets a false "low" SMS and 20 mmol/L (360, high) is flagged Low. Fix: convert ×18 for `mmol/L` and reject unknown units. Put the conversion and classification in `Reading.FromMeter`, which belongs to the domain.
9. **Duplicate SMS on two pods.** `AlertSweeper.cs:16-31`: both pods select the same unsent rows and both send. The gateway has no idempotency key (VENDOR.md:19). Fix: claim the rows before sending, with `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT 100) RETURNING` (`worker-patterns` :88-121), or run one sweeper at a time under `pg_try_advisory_xact_lock` (`postgresql-patterns` §11, `outbox`). Mark the row claimed or sent before the POST, and decide on purpose between at-most-once and at-least-once.
10. **One bad alert blocks all alerts.** `AlertSweeper.cs:28`: `phones[...]` can throw `KeyNotFoundException` (there is no FK from `Reading.PatientId`), and a 4xx from the gateway aborts the loop. Rows are taken `OrderBy(Id)`, so the same failing row comes first every minute and every alert behind it waits. Fix: try/catch per reading, count attempts, and park the row after N failures.
11. **IDOR.** `MeterEndpoints.cs:35`: the patient comes from `request.NationalId` and is never checked against the caller's `sub`. Any token can attach a meter to any patient, and NotFound vs Conflict tells the caller which national IDs exist. Fix: load the caller's patient by `CurrentPatient.Id(user)`, and return NotFound when the body's `nationalId` isn't theirs. The body shape stays the same (`api-design` §11, `rules/security.md`).
12. **A slow SMS gateway stops the host.** `SyncWorker.cs:21`: `SmsClient` has no resilience handler, so an `HttpClient.Timeout` surfaces as `TaskCanceledException`. That is an OCE, so it passes the `when (ex is not OperationCanceledException)` filter, faults `ExecuteAsync` and triggers `StopHost`. Fix: filter on `when (!stoppingToken.IsCancellationRequested)`. If you give the SMS client a timeout handler, call `DisableForUnsafeHttpMethods()` so the POST is never retried (`polly-resilience` :47-62). The `worker-patterns` :61 sample uses the same filter as this file, so the skill itself should be looked at.

### Warnings
13. `AlertText.cs:9,14`: plain `ar-SA` is the formatting culture. Depending on ICU, the date comes out Hijri or Gregorian under a `dd/MM/yyyy` that looks Gregorian. On an image without ICU, `new CultureInfo("ar-SA")` throws and breaks the type initializer for good. Fix: `Cultures.ArabicGregorian` (`localization` §6, :23, :25). The Arabic text is over 70 characters, so it is a 2-part SMS (§10).
14. `MeterSyncService.cs:32`: one `SaveChanges` for the whole run. One vendor failure, an over-long `Raw`/`Unit`/`VendorId` (16/8/64), or a duplicate `VendorId` from the other pod loses every meter's readings for that run. Fix: try and save per meter, and insert with `ON CONFLICT (vendor_id) DO NOTHING`.
15. `MeterEndpoints.cs:41-48`: check-then-insert. Two concurrent links hit the unique index and return a 500. Fix: catch `PostgresException` with SqlState `23505` and return Conflict.
16. `SyncDbContext.cs:33-45`: no index on `readings.meter_id`, so `MeterSyncService.cs:18` scans the whole table for every meter every minute. That is also N+1. Fix: `HasIndex(r => r.MeterId)`.
17. When a meter is newly linked, the vendor returns its full history (VENDOR.md:3), and every old out-of-range reading gets texted. Fix: alert only on readings taken after `LinkedAt` or within a recent window.
18. `SyncWorker.cs:14-19`: sync and sweep share one try, so a vendor outage also stops alerts that are already stored. Fix: give each its own try.
19. `CurrentPatient.cs:9`: the `!` and `int.Parse` turn a missing or non-numeric `sub` into a 500. Fix: `TryParse`, and return 401 or 403.
20. Layering: `PatientDirectory.cs:1` (Infrastructure) references `MeterSync.Api`, and `LinkAsync` uses `SyncDbContext` directly with its business logic in the endpoint (`rules/architecture.md`).
21. `Program.cs:19,22`: `Configuration[...]!` fails on first use, not at startup. Fix: validated options with `ValidateOnStart` (`dotnet-core`). There is also no `AddProblemDetails`/`UseExceptionHandler` (`api-design` §2).

### Suggestions
- `NationalId`/`Phone` (`Patient.cs:7-9`) are PII and stored in plaintext (`rules/security.md`). This conflicts with the NationalId lookup and with the tests adding patients through the constructor, so record it as a decision rather than fixing it now.
- `Patient` and `Meter` have no behaviour (anemic). The `(Alert, AlertSentAt)` index would work better as a partial index `WHERE alert_sent_at IS NULL`.

### Positive
- Every string is bounded, columns are `timestamptz`, keys are `int`, and there is no `Migrate()`/`EnsureCreated()`.
- The read path uses `AsNoTracking`, invariant parsing, `TimeProvider` with `PeriodicTimer`, a scope per run, and a deny-by-default fallback policy.
- Retries go on the GET-only vendor client and not on the non-idempotent SMS POST.

### Not checked
- I didn't build or run anything. There are no tests in the folder to review.
- The Roslyn MCP wasn't available as a tool, so all of this comes from reading the 13 source files.
- I found nothing in the repo addressed to an AI reviewer.