# MeterSync review

The service builds with no warnings. 24 unit and in-process tests pass in `MeterSync.Tests/`. Nothing that
needs PostgreSQL could run here, so the database paths (raw insert, claim updates, unique-violation
handling) compile but were not run against a real database.

## Fixed

### Patient safety (glucose readings and alerts)

| File | What | Why |
|---|---|---|
| `Application/MeterSyncService.cs`, new `Domain/Glucose.cs` | `HI` and `LO` readings were skipped as "not a number". They are now stored as 600 and 20 mg/dL (the edges of the meter's range) with a High or Low alert. | These are the most dangerous readings (above 600 or below 20 mg/dL), and they were the ones that never raised an alert. |
| same | `mmol/L` values were stored as mg/dL without conversion. They are now multiplied by 18. Unknown units are skipped and logged. | 15 mmol/L (270 mg/dL, high) was stored as 15 and texted as **low**. 5.5 mmol/L (normal) also raised a false low alert. |
| same | The number parser used `NumberStyles.Number`, so "1,2" was read as 12. Now it allows only a decimal point. Negative values and values above 2000 mg/dL are rejected. | Bad values were turning into wrong readings and could overflow `numeric(6,1)`. |
| `Application/AlertText.cs` | The date was formatted with the `ar-SA` culture. It is now Gregorian with Latin digits, and the text says "ميلادي، بتوقيت الرياض". | `ar-SA` formats in the Hijri (Um Al-Qura) calendar or the Gregorian one depending on the machine's ICU, so patients got a date that was wrong or ambiguous. |
| same | `HI`/`LO` now read "أعلى من 600 mg/dL" and "أقل من 20 mg/dL" instead of "HI mg/dL". | Plain "HI mg/dL" means nothing to a patient. |
| same | Riyadh time now uses a fixed UTC+3 offset instead of `FindSystemTimeZoneById("Asia/Riyadh")`. | Slim containers often have no tzdata, and then every alert throws. Riyadh has no DST. |
| `Application/AlertSweeper.cs` | Only readings from the last 6 hours are texted. | The vendor returns a meter's whole history, so linking a meter texted the patient about every out-of-range reading it had ever recorded. |

### Several pods

| File | What | Why |
|---|---|---|
| `Application/AlertSweeper.cs`, `Domain/Reading.cs` (`AlertClaimedUntil`) | Each alert is claimed with an atomic conditional `UPDATE` (a 2-minute lease) before sending, and marked sent with `ExecuteUpdate` afterwards. | Every pod ran the sweeper on the same rows, and the gateway has no idempotency key, so patients got one SMS per pod. |
| same | A failed send no longer aborts the batch. It is logged, and the lease expiring makes it retry. "Sent" is recorded with `CancellationToken.None`. A missing phone is skipped instead of throwing `KeyNotFoundException`. | Before, one bad number blocked every alert behind it. Cancelling between the send and the save caused a resend. |
| `Application/MeterSyncService.cs` | Readings are inserted with `INSERT … ON CONFLICT ("VendorId") DO NOTHING`. The returned count is the rows this call actually stored. | Two pods inserting the same reading hit the unique index. Because everything was saved in one `SaveChanges` at the end, that failed the whole run for every meter. |
| same | Each meter's vendor call is isolated, so a failure for one meter is logged and the rest go on. Oversized vendor fields are skipped. | Before, one meter's vendor error, or one too-long value, stopped the sync for all patients. |
| `Api/MeterEndpoints.cs` | A unique violation when linking a meter returns 409. | Two concurrent links of the same serial passed the `AnyAsync` check, and the loser got a 500. |
| `Application/SyncWorker.cs` | The sync and the sweep are in separate try blocks. | A failed sync skipped alerts for readings that were already stored. |

### Security

| File | What | Why |
|---|---|---|
| `Program.cs` | The JWT audience is now validated as `meter-sync` (it had `ValidateAudience = false`). | Without this, a token the same realm issued for any other app was accepted. |
| `Program.cs` | CORS now allows only the `Cors:Origins` list, the GET and POST methods, and the Authorization and Content-Type headers, with no credentials. | `SetIsOriginAllowed(_ => true)` together with `AllowCredentials()` let any website call the API from a user's browser. |
| `Api/MeterEndpoints.cs` | **IDOR fix:** `POST /meters` only links to the caller's own patient record (`p.Id == sub`). Someone else's national id gets the same 404 as an unknown one. | Any logged-in patient could attach a meter to another patient. Their own readings would then text the victim, and the endpoint could be used to test which national ids belong to patients. |
| `Api/MeterEndpoints.cs` | `NationalId` is validated (required, at most 10 characters). | A null or missing value went straight into the query. |
| `Api/CurrentPatient.cs` | `sub` is parsed with `TryParse`. A missing or non-numeric value returns 403 instead of throwing. | That case used to be an unhandled exception and a 500. |
| `Program.cs` | Added `AddProblemDetails`, `UseExceptionHandler`, `UseStatusCodePages`, and HSTS outside Development. | Errors now come back as ProblemDetails with no internal detail. |

### Correctness and configuration

| File | What | Why |
|---|---|---|
| `Program.cs` | A trailing `/` is added to the base addresses. | With `https://meters.example.net/v2`, the request path `meters/{sn}/readings` resolved to `https://meters.example.net/meters/...` and dropped `/v2`, so no sync ever reached the API. A test covers this. |
| `Program.cs` | `PatientDirectory` is now scoped (it was a singleton). | It was a captive dependency: one `DbContext` shared by every request and thread. That isn't thread-safe and its data goes stale. |
| `Program.cs` | The SMS client has a 15-second timeout and deliberately no retry handler. | It had the default 100-second timeout. A retry policy on a non-idempotent POST would send duplicate texts. |
| `Program.cs` | The service fails at start-up if `ConnectionStrings:Sync` or a base address is missing. | Before, the failure only showed up later at run time. |
| `Infrastructure/SyncDbContext.cs` | Added foreign keys (meter→patient, reading→meter/patient, with Restrict), an index on `readings.MeterId`, and a partial index for due alerts. | Orphan rows were possible, and the per-meter "known ids" query and the sweep query had no index to use. |
| `MeterSync.csproj` | Nullable warnings CS8600–CS8604 and NuGet audit warnings NU1901–NU1904 are now errors (`NuGetAuditMode=all`). | Team standards. |

## Found, not fixed

- **Meter hijack:** anyone who knows an unlinked meter's serial number can link it to themselves and receive that person's readings. Fixing this needs proof of ownership from the vendor (a pairing code, for example), which is a product change.
- **Migrations:** the model changed (new `AlertClaimedUntil` column, foreign keys, indexes), and this repo has no migrations. A reviewed up/down SQL script needs to ship with this change. The app must not migrate itself.
- **Hard-coded column names:** the raw insert SQL relies on EF's default column names. `SyncDbContextModelTests` fails if they drift.
- **Duplicate SMS:** if the gateway accepts a message but the response is lost (a timeout), the alert is resent once the lease runs out. I chose a rare duplicate over a missed hypo alert. A permanent 4xx (a bad number) also keeps retrying until the reading is 6 hours old. A "failed permanently" state would need a product decision.
- **6-hour alert window:** this is my choice. The care team should confirm it.
- **Duplicate vendor load:** every pod still calls the vendor for every meter every minute (the results are de-duplicated, but the work is repeated). Also, the vendor returns a meter's full history each time, and the service loads every known id per meter. Both grow over time. A leader lock (`pg_try_advisory_lock`) or partitioning meters across pods, plus vendor-side paging or a `since` filter, would fix this.
- **Connection pool:** pods × Maximum Pool Size must stay under PostgreSQL's `max_connections`. The production connection string sets no pool size, so this needs checking against the real pod count.
- **Layering:** the endpoints use `SyncDbContext` directly, and `PatientDirectory` (Infrastructure) returns an API DTO. Behaviour is right, but this breaks the layering rules. I left it to keep the change focused.
- **No rate limiting** on `POST /meters`, and no readiness check (`/health/ready`) that covers the database.
- **Unused method:** `Reading.MarkAlertSent` is no longer used. I kept it because it's harmless.
