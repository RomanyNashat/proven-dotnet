# MeterSync review

The service builds. `MeterSync.Tests/` has 33 unit and host tests, and all of them pass. They cover parsing, the SMS text, the schema, the vendor URL, SMS retries, CORS, the JWT audience and auth. Nothing that needs PostgreSQL was run here, so the claim logic between two pods and the handling of duplicate inserts have **not** been run against a real database.

## Fixed

### Safety: wrong or missing alerts
1. **`Domain/Reading.cs`: mmol/L readings were compared to mg/dL thresholds.** A reading of 15 mmol/L (270 mg/dL, high) was flagged *Low*, and a high in mmol/L could never alert. Values are now converted to mg/dL (×18) before classifying, and the unit is normalised. An unknown unit is skipped and logged.
2. **`Application/MeterSyncService.cs` + `Reading.cs`: `HI`/`LO` readings were thrown away as "not a number".** `HI` (over 600 mg/dL) is the most dangerous reading a meter can show. `HI` now raises a High alert and `LO` a Low one. `ValueMgDl` is null for these and the text says "above 600 / below 20 mg/dL".
3. **`MeterSyncService.cs`: the value parser used `NumberStyles.Number`.** That accepts thousands separators, so `"1,2"` read as 12, and it accepts negative numbers. It now allows a decimal point only, and the value must be positive and fit the column.
4. **`MeterSyncService.cs` / `Reading.cs`: a newly linked meter texted the patient about every high and low in its history.** The vendor returns every reading the meter ever uploaded. Readings from before the meter was linked, or more than 6 hours old when first seen, are now stored as `Stale` and not texted.
5. **`Application/AlertText.cs`: dates were formatted with `ar-SA`.** That culture's default calendar is Hijri (Um Al-Qura) on some ICU builds and Gregorian on others, so pods could text different years ("1448" vs "2026"). The text now uses invariant digits, the Gregorian calendar, and names the calendar ("ميلادي") and the time zone. It also used `FindSystemTimeZoneById("Asia/Riyadh")`, which throws on images without tzdata. Riyadh is now a fixed UTC+3 (no DST).

### Duplicate SMS / several pods
6. **`Application/AlertSweeper.cs`: two pods sent the same alert twice.** Both read the same "unsent" rows and texted them, and the gateway has no idempotency key. Each alert is now claimed before sending (claim lease plus an `xmin` row-version check). Only the pod whose claim commits sends the SMS. A pod that dies mid-send loses the claim after 5 minutes.
7. **`AlertSweeper.cs`: one failed SMS (or a missing phone number, which threw `KeyNotFoundException`) aborted the whole batch.** Failures are now handled one reading at a time. A failure keeps the claim, so the retry waits for the lease to lapse, and the reading is marked `Failed` after 5 attempts so it doesn't retry forever. The "sent" mark is saved even during shutdown. `RunOnceAsync` now returns how many SMS were actually sent.
8. **`MeterSyncService.cs`: two pods syncing at once hit the unique `VendorId` index, and the single `SaveChanges` at the end threw.** That lost every meter's readings for the run. Readings are now saved per meter. A unique violation is retried once against what the other pod stored, and one failing meter no longer stops the others. The known-ID lookup now queries only the IDs the vendor returned, not the whole history for each meter.
9. **`Api/MeterEndpoints.cs`: linking the same meter on two requests at once returned a 500 (unique violation).** It now returns 409.

### Security
10. **`MeterEndpoints.cs`: `POST /meters` let any logged-in user link a meter to *any* national ID (IDOR).** That meant one patient's readings and alerts could be attached to another patient. The national ID must now belong to the caller (`sub`). A mismatch gets the same 404 as an unknown ID, so the endpoint can't be used to find out who is a patient. `NationalId` is now validated too.
11. **`Program.cs`: the JWT audience was not validated (`ValidateAudience = false`).** A token issued by the same realm for any other app was accepted. It now requires the audience `meter-sync`, with `RequireHttpsMetadata` set explicitly.
12. **`Program.cs`: CORS allowed every origin *with credentials*.** Any website could call the API with the user's credentials. CORS now allows only the `Cors:Origins` list, with only the methods and headers the dashboard needs.
13. **`Api/CurrentPatient.cs`: a token whose `sub` was missing or not a number caused a 500.** It now returns 401.
14. **`Program.cs`: there was no global exception handler, so error responses weren't ProblemDetails.** Added `AddProblemDetails` and `UseExceptionHandler`, plus HSTS outside Development.
15. **`MeterSync.csproj`: nothing enforced zero known-vulnerable packages.** NuGetAudit is now on (`all`, with NU1901–NU1904 as errors), and nullable warnings are errors. There are no vulnerable packages today.

### Correctness and robustness
16. **`Program.cs`: the vendor calls went to the wrong URL.** The base address `https://meters.example.net/v2` has no trailing slash, so `meters/{sn}/readings` resolved to `https://meters.example.net/meters/...` and dropped `/v2`. Base addresses now get a trailing slash and must be absolute https URLs. A host test checks the exact URL.
17. **`Program.cs`: `PatientDirectory` was a singleton holding the scoped `SyncDbContext` (captive dependency).** In Production, one DbContext was shared across concurrent `GET /meters` requests, which causes "second operation started on this context" errors and stale data. It is now scoped. `AlertSweeper` is now scoped too and takes its dependencies directly.
18. **`Program.cs`: the SMS client had the default 100 s timeout.** It now has a 15 s timeout. Retries are deliberately left off, because a retried POST sends a second SMS. A host test checks that a 500 is sent only once.
19. **`Application/SyncWorker.cs`: the catch excluded every `OperationCanceledException`.** An HttpClient timeout is one, so a single slow vendor or SMS call escaped `ExecuteAsync` and stopped the whole host (the .NET default is `StopHost`). The worker now rethrows only when shutdown was requested. The sweeper also runs in its own scope.
20. **`Infrastructure/SyncDbContext.cs`: no foreign keys, and identity columns were `BY DEFAULT`.** Added FKs (meter→patient, reading→meter, reading→patient), `GENERATED ALWAYS AS IDENTITY`, a filtered index for pending alerts, and indexes on `MeterId` and `PatientId`. New columns: `AlertStatus`, `AlertClaimedUntil`, `AlertAttempts`, and the `xmin` row version.
21. **`Infrastructure/PatientDirectory.cs`: Infrastructure referenced the API's `MeterResponse` type (wrong layer direction).** It now returns its own `LinkedMeter`, and the endpoint maps it to `MeterResponse`. The JSON shape is unchanged.
22. **`Infrastructure/MeterVendorClient.cs`: a null entry in the vendor's payload crashed the sync.** Null or incomplete entries are now dropped, and over-long IDs or values are skipped with a log line instead of failing the insert.

## Found, not fixed
- **No migration scripts.** The schema has changed (new columns, FKs, identity ALWAYS, indexes), and production needs a reviewed up/down SQL pair (`dotnet ef migrations script`). The hidden tests create the schema from the model, so I didn't add EF migrations here.
- **Small duplicate-SMS window remains.** If a pod sends the SMS and dies before saving "sent", or the gateway times out after accepting the message, the alert is sent again after the 5-minute lease. Without an idempotency key on the gateway this can't be fully closed. It's a trade-off: an occasional duplicate rather than a missed hypo or hyper alert.
- **Several out-of-range readings in a row mean several SMS.** There's no per-patient grouping or rate limit. That's a product decision for the care team.
- **The vendor returns a meter's whole history every minute.** The payload keeps growing. VENDOR.md documents no `since`/paging parameter, so this needs to go to the vendor.
- **Every pod polls the vendor every minute.** Results are idempotent now, but load scales with pod count. A leader lock (`pg_try_advisory_lock`) or a single CronJob would reduce it.
- **PII (national ID, phone) is stored in plain text** and isn't encrypted at rest. Encrypting it needs a deterministic or blind-index scheme for the national-ID lookup and a key-management decision.
- **No `UseHttpsRedirection`.** TLS is assumed to end at the ingress, and redirecting behind it needs forwarded-headers config. HSTS is on.
- **Leftover design issues:** business logic still lives in the endpoint handlers rather than an Application layer, there's no FluentValidation, entity configs are inside `OnModelCreating`, and the health check doesn't check the database. These matter less than the items above.
- **`/health/live` doesn't touch the database.** That's right for liveness, but there's no readiness probe.
