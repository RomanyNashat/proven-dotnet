## Code Review: MeterSync (/tmp/claude-0/syncruns/with-proven-1/MeterSync)

### Summary
I've finished the review that the usage limit cut off. **MeterSync is not ready for production.** As written, the service never syncs: the vendor base address drops `/v2`. If that were fixed, it would still skip the most dangerous readings and misread mmol/L ones. On two pods it double-texts patients. One failing SMS or meter blocks all later work. One SMS timeout can shut the pod down. All 6 rows in scan.md are confirmed; I raised 2 of them to critical, and found 22 more problems.

### Critical (must fix)
1. **[Clinical correctness]** `Application/MeterSyncService.cs:21-27` and `Domain/Reading.cs:50`. `HI` (over 600 mg/dL) and `LO` (under 20 mg/dL) fail `decimal.TryParse` and are skipped with a warning, so the most dangerous readings never raise an alert. `mmol/L` values are saved as mg/dL without conversion: 15 mmol/L (270 mg/dL) is stored as 15 and alerts as Low. **Fix**: in the domain, map HI to High and LO to Low (store 600/20 and keep `Raw`). Multiply mmol/L by 18. Reject any unit other than these two.
2. **[Multi-pod / duplicate SMS]** `Application/AlertSweeper.cs:16-31`. Every pod selects the same unsent readings and sends them, and the gateway has no idempotency key (VENDOR.md:19). Patients get the same SMS twice. **Fix**: claim each row before sending, either with a conditional `ExecuteUpdateAsync(r => r.Id == id && r.AlertSentAt == null, …)` where only rows-affected == 1 sends, or with `FOR UPDATE SKIP LOCKED` (`worker-patterns`, "With more than one pod"). Release the claim if the send fails.
3. **[One failure blocks everything]** `Application/AlertSweeper.cs:26-31`. A single `EnsureSuccessStatusCode` failure or a `phones[...]` KeyNotFound ends the sweep. The same reading comes first every minute, so no alert is ever sent again. `MeterSyncService.cs:15-32` has the same problem: one meter's vendor 404 ends the run, and the single `SaveChanges` at the end loses everything gathered before it. **Fix**: catch errors per item and log them. Count failed attempts and park the reading after N. Save once per meter.
4. **[Multi-pod race]** `Application/MeterSyncService.cs:18,32`. Two pods read the same known IDs and both insert the new ones. The unique index on `VendorId` (`SyncDbContext.cs:43`) then fails the whole `SaveChanges`. **Fix**: `INSERT … ON CONFLICT (vendor_id) DO NOTHING` (`postgresql-patterns` §8), or a pg advisory lock so only one pod syncs.
5. **[Vendor URL]** `appsettings.json:6` (scan row, raised from warning to critical). `https://meters.example.net/v2` + `meters/…` resolves to `/meters/…`, so every vendor call 404s. Production keeps this value (task.md:11), so the config can't change. **Fix in code** at `Program.cs:19`: add a trailing `/` when it's missing. Don't validate and refuse it the way `dotnet-core` line 72 does, because that would stop production from starting.
6. **[Localization]** `Application/AlertText.cs:9,13-14` (scan row :13, raised from warning to critical). On an image without ICU, `new CultureInfo("ar-SA")` throws in a static field, so every alert fails with TypeInitializationException (and finding 3 makes that block all alerts). With ICU, the date is Hijri or Gregorian depending on the machine's ICU, and the SMS doesn't say which. Without tzdata, `FindSystemTimeZoneById` throws. **Fix**: Riyadh has no DST, so use `TakenAt.ToOffset(TimeSpan.FromHours(3))`, or the §7 fallback. Format with `CultureInfo.InvariantCulture` (Gregorian, Latin digits) (`localization` table rows 23/25/26, §6, §7).
7. **[Broken object-level authorization (IDOR)]** `Api/MeterEndpoints.cs:24-48`. `POST /meters` never looks at the caller. Any signed-in patient can link a meter to anyone's national ID, or link someone else's meter to themselves and then read that person's glucose data. The 404/409 answers also tell an attacker which national IDs exist. **Fix**: take `ClaimsPrincipal` and return NotFound unless `patient.Id == CurrentPatient.Id(user)` (`api-design` §11, `rules/security.md`). The body shape stays the same.
8. **[DI captive dependency]** `Program.cs:13` (scan, confirmed). A singleton `PatientDirectory` holds one DbContext for every request, so concurrent `GET /meters` calls throw "a second operation was started" and return stale data. **Fix**: `AddScoped` (`dotnet-core`).
9. **[CORS]** `Program.cs:38-42` (scan, confirmed). This reflects any origin with credentials. **Fix**: `WithOrigins(config.GetSection("Cors:Origins").Get<string[]>())` and drop `AllowCredentials`, since this is a bearer API (`owasp-aspnetcore` lines 89-111).
10. **[Worker kills the host]** `Application/SyncWorker.cs:21`. `SmsClient` has no resilience handler, so a slow gateway hits the 100 s HttpClient timeout. That throws `TaskCanceledException`, which is an OperationCanceledException, so it gets past the filter, the worker faults, and `StopHost` shuts the pod down. **Fix**: `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)`. Harness note: the `worker-patterns` sample at line 61 uses the same filter and has the same gap.

### Warnings (should fix)
1. `Program.cs:28-32` (scan, confirmed). `ValidateAudience = false`. Set `o.Audience = "meter-sync"` and validate it (`auth-patterns` lines 51, 124).
2. `Program.cs:21-22`. The SMS client has no timeout. Add a total timeout but no retry, because a POST sends every time (`polly-resilience` lines 47, 60: `DisableForUnsafeHttpMethods`).
3. `Application/SyncWorker.cs:14-19`. Sync and sweep share one try block, so a vendor outage also stops alerts for readings already stored. Give each its own try.
4. `Application/MeterSyncService.cs:27`. A newly linked meter brings in its whole history, so the patient gets texted about old readings and possibly a previous owner's. Only alert on readings taken after `LinkedAt` and within a recent window.
5. `Application/MeterSyncService.cs:13-18`. Each meter costs one vendor call plus one query that loads every VendorId ever stored for it, and there is no index on `MeterId`. The runs are sequential, so with many meters a run takes longer than the 1-minute tick. Query `VendorId IN (returned ids)` instead.
6. `Api/MeterEndpoints.cs:41-48`. If two links race, the loser gets a unique violation and a 500. Catch SqlState 23505 and return Conflict.
7. `Infrastructure/SyncDbContext.cs:24-45`. There are no foreign keys between meters, readings and patients, because there are no navigations. Add `HasOne().WithMany().HasForeignKey()`.
8. `Infrastructure/SyncDbContext.cs:37-38`. A vendor `value` over 16 characters or a `unit` over 8 fails the batch. Validate these before saving.
9. `Program.cs:9` (scan info, confirmed). Turn on `ValidateScopes` and `ValidateOnBuild` in every environment.
10. `Program.cs:19,22,27`. Config is read with `!`. Use bound options with `ValidateOnStart` (`dotnet-core`).
11. `Api/CurrentPatient.cs:9`. If `sub` is missing or isn't a number, `!` plus `int.Parse` gives a 500. Use `TryParse` and return 401.
12. `Api/MeterEndpoints.cs:27`. `NationalId` isn't validated (null, or longer than 10).
13. `Application/MeterSyncService.cs:23`. A glucose value goes into the logs, which is health data (`rules/security.md`).
14. `Infrastructure/PatientDirectory.cs:1,8`. Infrastructure depends on `Api.MeterResponse`, and Application uses `SyncDbContext` directly. Both break the layer rules.

### Suggestions
- `AlertText.cs:16`. The Arabic SMS is over 70 characters, so it sends as 2 parts (`localization` §10).
- There is no `UseExceptionHandler`/ProblemDetails.
- Use a partial index for unsent alerts.
- `Patient` and `Meter` have no behavior (anemic models).
- The repo has no reviewed migration scripts and no tests.

### Positive
- `GET /meters` filters by the caller.
- The fallback policy denies by default, and `ClockSkew` is 30 s.
- `TimeProvider` and `PeriodicTimer` are used.
- `AlertSweeper` uses `IServiceScopeFactory`.
- Read queries use `AsNoTracking`.
- Every string column is bounded, timestamps are `timestamptz`, and keys are `int`.
- Nothing calls `Migrate` or `EnsureCreated`.
- Numbers are parsed with the invariant culture, and the serial number is passed through `Uri.EscapeDataString`.
- Sent alerts are saved one at a time, so a crash mid-sweep doesn't resend the whole batch.

**Not checked:** I didn't build or run anything, because I'm read-only, so I couldn't check for vulnerable packages either. The Roslyn MCP wasn't used; the service has 13 small files, and I read all of them. The nginx forwarded-headers check doesn't apply, because nothing reads the client IP.