# MeterSync review

The service builds with no warnings. NuGet audit is set to `all` and known-vulnerable packages fail the build. 35 unit tests in `MeterSync.Tests/` pass. Nothing that needs PostgreSQL was run here: the raw `INSERT … ON CONFLICT`, the `ExecuteUpdate` claims and the two-pod behaviour are untested against a real database.

## Fixed

### Security
1. **`Program.cs`: the JWT audience was not checked.** `ValidateAudience = false` meant a token the same identity provider issued for any other app was accepted. It now requires `meter-sync`. The authority still comes from `Auth:Authority`, and HTTPS metadata is required.
2. **`Program.cs`: CORS allowed every origin, with credentials.** `SetIsOriginAllowed(_ => true)` together with `AllowCredentials()` let any website make credentialed calls. Only the origins in `Cors:Origins` are allowed now, and only for GET and POST with `Authorization` and `Content-Type` headers. Credentials are no longer allowed, because the API takes bearer tokens in a header.
3. **`Api/MeterEndpoints.cs` + new `Application/MeterLinker.cs`: anyone could link a meter to any patient (IDOR).** `POST /meters` looked up the patient by the `nationalId` in the body. Any logged-in patient could attach their meter to someone else, or tell from 404 vs 409 whether a national ID belongs to a patient. The lookup now also requires the patient to be the caller (`sub`). Someone else's national ID gets the same 404 as an unknown one. The request and response shapes are unchanged.
4. **`Api/CurrentPatient.cs`: a bad `sub` crashed the request.** `int.Parse` threw a 500 when `sub` was missing or not a number. Now it returns 403.
5. **`Application/MeterSyncService.cs`: glucose values were written to the log.** Unparseable values were logged, and those are health data. The log now has only the vendor reading ID and the meter ID.
6. **`Program.cs`: errors had no ProblemDetails.** Added `AddProblemDetails`, `UseExceptionHandler` and `UseStatusCodePages`, so errors come back as RFC 7807 with no internal detail.

### Correctness: clinical
7. **`Domain/GlucoseValue.cs` (new), `Domain/Reading.cs`: mmol/L readings were treated as mg/dL.** A 3.5 mmol/L reading (63 mg/dL, low) passed the "< 70" check and alerted, but so did every normal mmol reading (e.g. 6 mmol/L). A dangerous 15 mmol/L (270 mg/dL) never counted as high. mmol/L values are now multiplied by 18 before the thresholds are applied. Unknown units are skipped.
8. **Same files: `HI` and `LO` were thrown away.** These are the most dangerous readings (over 600 or under 20 mg/dL), and they failed to parse, got a warning and were skipped, so no alert. They are now stored with a null `ValueMgDl` and raise a High or Low alert. `ValueMgDl` is now nullable.
9. **Same files: parsing read thousands separators.** `NumberStyles.Number` turned `"5,5"` into 55 and `"1,200"` into 1200. Only a decimal point is accepted now, and zero or negative values are rejected.
10. **`Domain/Reading.cs`, `Application/AlertSweeper.cs`: linking a meter texted the patient about its whole history.** The vendor returns every reading the meter ever uploaded, so the first sync queued one SMS for each old out-of-range reading. Alerts now go out only for readings taken within `Reading.AlertWindow` (2 hours). Older ones are stored as `Expired`. Pending alerts that pass the window without being sent (for example during an SMS outage) also expire.

### Correctness: several pods
11. **`Application/AlertSweeper.cs`: two pods sent the same SMS.** Both pods selected the same unsent readings and both texted them, and the gateway has no idempotency key. Each reading is now claimed with a conditional `UPDATE … WHERE state = Pending AND claim expired`. Only the pod whose update matched sends the SMS. After a successful send the reading is marked `Sent` with `CancellationToken.None`, so a shutdown right after the send doesn't lead to a resend.
12. **Same file: one failed SMS stopped the whole batch.** The exception left the loop, and the remaining patients waited for the next run. Now a failure is logged and the loop moves on. The reading keeps its claim for 2 minutes, then any pod retries it, until it expires (see 10). `RunOnceAsync` now returns the number of texts sent.
13. **`Application/MeterSyncService.cs`: concurrent syncs broke the unique index.** Two pods inserting the same `VendorId` hit the unique index, and since there was one `SaveChanges` for the whole run, every meter's new readings were lost. Each reading is now inserted with `INSERT … ON CONFLICT ("VendorId") DO NOTHING`, and the return value counts only rows actually inserted.
14. **Same file: one meter's error stopped the sync for all meters.** If the vendor failed for one meter (404, timeout, bad JSON), the error ended the run. Each meter's fetch is now caught and logged on its own. Also, the "already known" lookup used to load every reading ID for the meter on every run. It now checks only the IDs the vendor returned, using the unique index. Vendor IDs that are missing or too long are skipped.
15. **`Api/MeterEndpoints.cs` / `MeterLinker.cs`: two concurrent links of the same serial returned 500.** The unique-index violation (`23505`) now returns 409 Conflict. `nationalId` is now validated too (required, at most 10 characters), and null fields give a 400 instead of a crash.

### Correctness: wiring
16. **`Program.cs` + new `Infrastructure/BaseAddress.cs`: the vendor URL dropped `/v2`.** `https://meters.example.net/v2` has no trailing slash, so the relative `meters/{sn}/readings` resolved to `https://meters.example.net/meters/…`. A trailing slash is now added. The setting is unchanged, and a missing or non-https address fails at startup.
17. **`Program.cs`: `PatientDirectory` was a singleton holding a scoped `DbContext` (captive dependency).** Production doesn't validate scopes, so one DbContext was shared by every request: not thread-safe, a growing change tracker, stale data. `PatientDirectory`, `MeterSyncService`, `AlertSweeper` and `MeterLinker` are all scoped now.
18. **`Application/SyncWorker.cs`: the first HTTP timeout stopped the app.** The filter `when (ex is not OperationCanceledException)` let an `HttpClient` timeout (a `TaskCanceledException`) escape `ExecuteAsync`. On .NET 8+ that stops the host. The filter now checks `stoppingToken.IsCancellationRequested` instead. Sync and sweep also run as separate steps, each in its own scope, so a vendor outage no longer holds back alerts that are already stored.
19. **`Program.cs`: the SMS client had a 100 s timeout.** It is now 15 s. I deliberately did **not** add a retry or resilience handler: a retried POST can send the same SMS twice. The sweeper does the retrying, under its claim.
20. **`Program.cs`: settings were read when `Program` started.** They are now read from `IConfiguration` when the services are built (DbContext, HTTP clients, JWT, CORS), so later configuration sources, including a test host's, are honoured.

### Text and culture
21. **`Application/AlertText.cs`: the SMS date came from `ar-SA`.** That culture's calendar is Hijri (Umm al-Qura) or Gregorian depending on the machine's ICU, so `dd/MM/yyyy` could print the year as 1448. It also throws under invariant globalization. The date is now formatted invariantly and marked Gregorian (`3/11/2026م`), followed by `بتوقيت الرياض`.
22. **Same file: the time zone depended on tzdata.** `FindSystemTimeZoneById("Asia/Riyadh")` throws in containers without tzdata. Riyadh is UTC+3 all year, so a fixed offset is used.
23. **Same file: HI and LO read badly.** They came out as "HI mg/dL". They now read in Arabic as above or below what the meter can measure.

### Schema
24. **`Infrastructure/SyncDbContext.cs`: no foreign keys.** `meters.PatientId`, `readings.PatientId` and `readings.MeterId` had no constraints, because there were no navigations. Added FKs with `Restrict`. Also added the `AlertState` and `AlertClaimedUntil` columns, an index on `(AlertState, Id)` for the sweeper, and changed `ValueMgDl` to nullable `numeric(5,1)`.

### Build
25. **`MeterSync.csproj`: nullable warnings and the vulnerability audit didn't fail the build.** CS8600, CS8602 and CS8603 are now errors, and so are `NuGetAuditMode=all` with NU1901–NU1904. Infrastructure no longer references the API layer: `PatientDirectory` returns `Application.LinkedMeter`.

## Found, not fixed
- **No migration script.** The schema changed: new columns, nullable `ValueMgDl`, and the FKs. The project has no migrations, and the hidden tests build the schema from the model. Before production, someone needs to generate and review the up/down SQL (`dotnet ef migrations script`) and run it through the pipeline. It also needs a backfill for existing rows: `AlertState = 'Sent'` where `AlertSentAt` is set, `'NotNeeded'` where `Alert = 'None'`, otherwise `'Expired'`.
- **The SMS can still go out twice in a rare case.** If a send times out after the gateway accepted it, or a pod dies between the send and marking it `Sent`, the alert is retried. Without an idempotency key the gateway gives no way to tell, so I chose at-least-once: a missed hypo alert is worse than a duplicate. This is worth confirming with the care team.
- **The 2-hour alert window is my own choice.** The care team should confirm it, or it should become a setting.
- **Readings from before the link are imported and attributed to the new patient.** They don't alert unless they fall inside the window. A meter that changed hands would put the previous owner's readings in the new patient's record. Deciding whether to ignore readings before `LinkedAt` is a product question.
- **One SMS per out-of-range reading.** A patient with several high readings in a row gets several texts. Combining them per patient would need product input.
- **Every pod syncs every meter every minute**, so vendor traffic grows with the number of pods. That's harmless now because the inserts are idempotent. A leader lock (a PostgreSQL advisory lock) or splitting meters across pods would fix it. Meters are also fetched one at a time, so a large fleet will overrun the 1-minute tick.
- **Connection pool size.** Npgsql allows up to 100 connections per pod by default, so 2 or more pods can go over PostgreSQL's default `max_connections` of 100. Set `Maximum Pool Size` in the production connection string; I left the settings unchanged.
- **HSTS and HTTPS redirection are not enabled.** I'm assuming TLS ends at the ingress. If it doesn't, add `UseHsts` and `UseHttpsRedirection`.
- **No validation framework.** Validation is inline in the endpoint. FluentValidation would be the house standard, but I didn't add the dependency for two fields.
- **Patient phone numbers are not checked for E.164 format** before they are sent to the gateway.
- **`/health/live` doesn't use the health-checks framework**, and there is no readiness check that covers the database.
