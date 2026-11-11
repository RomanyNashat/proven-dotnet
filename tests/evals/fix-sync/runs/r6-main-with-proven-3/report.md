# MeterSync: review and fixes

How it was done: `/full-review` (the known-traps scan plus code, security and DBA reviewers; full findings in `review-findings.md`), then tdd-guide applied the fixes test-first.

The service builds. 105 unit tests pass in `MeterSync.Tests/` (xUnit, FluentAssertions `[7.0.0,8.0.0)`). Line coverage is 72.4%, branch 72.9%. What isn't covered needs PostgreSQL: the sync insert, the alert claim and the endpoint handlers.

## Fixed

### Patient safety (wrong or missing alerts)
| File | What | Why |
|---|---|---|
| `Program.cs`, `Infrastructure/BaseAddress.cs` | Adds a trailing `/` to the base address in code. The setting stays `https://meters.example.net/v2`. | Without the slash, `meters/{serial}/readings` resolved to `https://meters.example.net/meters/...` and lost `/v2`. Every vendor call went to the wrong URL, so no reading was ever synced. |
| `Domain/Reading.cs` (`TryFromMeter`) | `HI` is stored as 600 mg/dL with a High alert, `LO` as 20 mg/dL with a Low alert. | `HI`/`LO` failed `decimal.TryParse` and were skipped. The most dangerous readings (over 600, under 20) never alerted. |
| `Domain/Reading.cs` | mmol/L is converted to mg/dL (×18, one decimal). Unknown units are rejected. | Values were compared with the mg/dL thresholds unconverted: every mmol/L reading looked Low, and a real 15 mmol/L high was texted as low. |
| `Domain/Reading.cs` | Parsing uses `NumberStyles.AllowDecimalPoint`. It rejects too-long raw values and values outside `numeric(6,1)`. | `NumberStyles.Number` read `"1,2"` as 12. Bad lengths failed the whole save. |
| `Application/AlertText.cs`, `ArabicCulture.cs`, `RiyadhTime.cs` | Dates are Arabic on an explicit Gregorian calendar with month names (`2 نوفمبر 2026 10:58`) and Latin digits. Riyadh time falls back to a fixed UTC+3 when tzdata is missing. HI/LO read as "أعلى من 600" / "أقل من 20". | Plain `ar-SA` takes its calendar from the machine's ICU: Hijri (year 1448) on one image, Gregorian on another. A missing time zone threw before any SMS was sent. "HI mg/dL" means nothing to the patient. |

### Several pods sharing one database
| File | What | Why |
|---|---|---|
| `Application/AlertSweeper.cs`, `Domain/Reading.cs`, `SyncDbContext.cs` | New `AlertClaimedAt` lease. Each sweep claims up to 100 rows in one autocommitted `UPDATE … WHERE Id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING Id`, sends, then marks each row sent. A failed send releases the row. A claim from a pod that died expires after 5 minutes. No transaction is held across the HTTP call. | Every pod selected and texted the same rows. The gateway has no idempotency key, so patients got one SMS per pod. |
| `Application/MeterSyncService.cs` | Each reading is inserted with a parameterized `INSERT … ON CONFLICT ("VendorId") DO NOTHING`, and `added` counts the rows actually inserted. Known IDs are looked up only for the incoming IDs, into a `HashSet`. | Check-then-insert raced between pods. The unique violation threw away the losing pod's whole run on every tick. The old lookup loaded a meter's entire history each minute. |
| `Api/MeterEndpoints.cs` | A unique violation (23505) on linking returns 409. | Two concurrent links of the same serial gave a 500. |
| `Program.cs` | `PatientDirectory` and `AlertSweeper` are scoped. `ValidateScopes`/`ValidateOnBuild` are on in every environment. | The singleton `PatientDirectory` held one DbContext for every request. Concurrent `GET /meters` calls threw. Production skips scope validation, so nothing caught it. |

### Resilience
| File | What | Why |
|---|---|---|
| `Application/SyncWorker.cs` | Sync and sweep each get their own scope and their own catch, filtered `when (!stoppingToken.IsCancellationRequested)`. | An HttpClient timeout is a `TaskCanceledException`. It got past the old filter, faulted the worker, and the default `StopHost` took the pod down. A failed sync also skipped the sweep. |
| `Application/MeterSyncService.cs` | Errors are caught per meter. | One meter failing (a mistyped serial returning 404, a timeout) stopped the sync for every meter. |
| `Application/AlertSweeper.cs` | Errors are caught per reading, and the row is released. | One failing SMS blocked every later alert, forever, because the rows are taken in Id order. |
| `Program.cs` | The SMS client has a 15 s timeout and no retry handler. | The default was 100 s. A retry would text the patient twice. |
| `Infrastructure/SyncDbContext.cs` | Foreign keys Meter→Patient, Reading→Meter and Reading→Patient. Indexes on `readings.MeterId` and `PatientId`. A partial index for the sweep. | Without FKs, orphan rows made the phone lookup throw. Each meter scanned the whole readings table. |

### Security
| File | What | Why |
|---|---|---|
| `Api/MeterEndpoints.cs` | `POST /meters` loads the caller's patient by `sub`. A national ID that isn't the caller's gets the same 404 as an unknown one. | IDOR: any logged-in user could link a meter to any patient, or link someone else's serial to themselves and receive their glucose alerts. The 404/201 difference also showed whether a national ID was registered. |
| `Program.cs` | The JWT audience `meter-sync` is validated. `MapInboundClaims = false`. | `ValidateAudience = false` accepted a token the realm issued for any other client. |
| `Program.cs` | CORS allows only `Cors:Origins`, without credentials, GET/POST, `Authorization`/`Content-Type`. | `SetIsOriginAllowed(_ => true)` with `AllowCredentials` let any site call the API. The `Cors:Origins` setting was never read. |
| `Api/LinkMeterRequest.cs` | National ID must be `^\d{10}\z`, serial `^[A-Za-z0-9_-]{1,40}\z`. | The national ID wasn't validated at all. A serial of `..` changed the path of the vendor URL. |
| `Api/CurrentPatient.cs`, `MeterEndpoints.cs` | `sub` is parsed with `TryParse`, and a bad value returns 401. | `int.Parse` on a missing or non-numeric `sub` was a 500. |
| `Program.cs` | ProblemDetails, the exception handler, status-code pages, HSTS, `nosniff`, `X-Frame-Options: DENY` and a CSP. | Errors were bare responses with no structure, and no security headers were sent. |
| `MeterSyncService.cs`, `AlertSweeper.cs`, `appsettings.json` | Logs carry meter, vendor and reading IDs only. `System.Net.Http.HttpClient` logs at Warning. | Glucose values were logged. Request logging put serial numbers in every vendor URL. |
| `MeterSync.csproj` | `NuGetAudit` on in `all` mode. NU1901–NU1904 and CS8600/8602/8603 are errors. | A vulnerable package should fail the build. No vulnerable or deprecated packages today. |

### Structure
- `MeterResponse` moved to `Application/`, so Infrastructure no longer references the Api layer.
- One type per file (`AlertKind`, `VendorReading`, `LinkMeterRequest`).

## Found, not fixed
- **Alerts for old readings.** The vendor returns a meter's whole history. On the first sync after linking, the patient is texted about every old out-of-range reading. If the meter had a previous owner, that owner's readings are stored as the new patient's. Whether to skip, store without alerting, or alert only recent readings is a care-team decision. Filtering on `TakenAt >= LinkedAt` is a one-line change once it's decided.
- **At least once, not exactly once.** If the gateway accepts a text and the save then fails, or the pod dies mid-send, the text goes again once the 5-minute lease expires. The gateway has no idempotency key, so exactly-once isn't possible from this side.
- **National ID and phone are in plain text at rest.** Encrypting them needs keys from a vault and a blind index for the unique national-ID lookup.
- **No rate limit on `POST /meters`.** Less urgent now that the national ID must match the caller.
- **Connection pool size and `SSL Mode`** aren't set in the connection string. They depend on the deployment: pods × pool size has to fit under `max_connections`.
- **Identity columns** are `BY DEFAULT`, not `GENERATED ALWAYS`.
- **Every pod polls the vendor for every meter each minute.** `ON CONFLICT` keeps this correct, but it wastes calls. The vendor API has no "since" parameter.
- **No migration scripts exist** (tests build the schema from the model). Production needs a reviewed up/down script for the new FKs, indexes and `AlertClaimedAt` column.

## Not verified here
There's no PostgreSQL here, so these are unverified:
- the raw SQL (the claim `UPDATE`, `ON CONFLICT`);
- the 409 path;
- behaviour with two instances.

Column names were only checked against the DDL EF generates. JWT validation with a real signed token isn't tested either.
