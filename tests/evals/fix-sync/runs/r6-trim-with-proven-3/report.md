# MeterSync: pre-production review and fixes

Process: `/full-review` (known-traps scan, then code, security and DBA reviewers in parallel, plus the package and coverage checks). The tdd-guide agent then applied the fixes test-first, and I reviewed its diff and made two small changes.

Result: `dotnet build` passes with 0 warnings. `dotnet test MeterSync.Tests` passes 104 of 104.

**Nothing that needs PostgreSQL has run here.** That covers:
- the `ON CONFLICT` insert
- the `ExecuteUpdate` claim in the alert sweeper
- the 409 on a duplicate serial
- two pods running at once

These compile, and the offline schema test confirms the column names the raw SQL uses, but they are untested against a real database.

## Fixed

### Patient safety (alerts)

| File | What | Why |
|---|---|---|
| `Application/MeterSyncService.cs`, `Domain/Reading.cs` | `HI` now gives a High alert and `LO` a Low alert. `ValueMgDl` is now nullable, and `Raw` keeps what the meter showed. | `HI` (over 600 mg/dL) and `LO` (under 20) failed `decimal.TryParse` and were skipped. The most dangerous readings never sent an alert. |
| `Domain/Reading.cs` | `mmol/L` values are multiplied by 18 before checking the thresholds. Unknown units are rejected with a warning. | 15 mmol/L (270 mg/dL, high) was stored as "15 mg/dL" and texted as **Low**. Every normal mmol/L reading also alerted Low. |
| `Domain/Reading.cs` | Values are parsed with `NumberStyles.AllowDecimalPoint` instead of `NumberStyles.Number`. | `Number` allows thousands separators, so `"1,2"` became 12. |
| `Domain/Reading.cs` | Readings taken before the meter's `LinkedAt` are stored but never alert. | The vendor returns the meter's whole history. Linking a meter would have texted the patient about months-old readings. |
| `Application/AlertText.cs` | Clones ar-SA with a `GregorianCalendar`, uses literal separators, and names the calendar and time zone in the text ("بتوقيت الرياض، تقويم ميلادي"). | ar-SA defaults to Um Al-Qura (Hijri), depending on the machine's ICU. The test showed `22‏/05‏/1448` for 2 Nov 2026. "م" was not used as the calendar marker because in ar-SA it is also the PM marker. |
| `Application/AlertText.cs` | `Asia/Riyadh` is resolved once, with a fixed UTC+3 fallback. | On an image without tzdata every alert threw `TimeZoneNotFoundException`. Saudi Arabia has no DST, so +3 is exact. |
| `Application/AlertText.cs` | HI/LO readings say "above/below the meter's range" instead of "HI mg/dL". | The message has to be readable. |

### Several pods sharing one database

| File | What | Why |
|---|---|---|
| `Application/AlertSweeper.cs`, `Domain/Reading.cs`, `Infrastructure/SyncDbContext.cs` | Each alert is claimed atomically before sending. An `ExecuteUpdate` sets `AlertClaimedAt` (5-minute lease) and adds 1 to `AlertAttempts`, and only the pod whose update hit the row sends. `AlertSentAt` is then saved with `CancellationToken.None`. | Every pod read the same 100 unsent rows and texted each one. The gateway has no idempotency key, so N pods meant N texts. |
| `Application/AlertSweeper.cs` | Each reading has its own try/catch, with at most 5 attempts. A missing phone is logged with ids only. | One bad number (4xx) or a missing patient (`KeyNotFoundException`) threw on every run at the oldest row, so no later alert was ever sent. |
| `Application/MeterSyncService.cs` | Inserts use `INSERT … ON CONFLICT ("VendorId") DO NOTHING`, parameterized through `ExecuteSqlAsync` with a `FormattableString`. Each meter has its own transaction, with rows in a fixed order. | Two pods inserting the same vendor reading hit the unique index. The single `SaveChanges` then rolled back every meter's new readings. |
| `Application/MeterSyncService.cs` | Each meter has its own try/catch. | One serial the vendor doesn't know (404) or a timeout stopped the sync for everyone. |
| `Application/MeterSyncService.cs` | Looks up only the incoming vendor ids, in a `HashSet`. | Before, it ran a query per meter that loaded every vendor id the meter ever had, into a list that only grew. |
| `Api/MeterEndpoints.cs` | Returns 409 when the insert fails on the unique serial (`23505`, through `Infrastructure/PostgresErrors.cs`). | The check-then-insert race between two requests or two pods returned 500. |

### Runtime correctness

| File | What | Why |
|---|---|---|
| `Program.cs` | `PatientDirectory` is now scoped. `ValidateScopes` and `ValidateOnBuild` are on in every environment. | A singleton held one `SyncDbContext` for the whole process. Concurrent `GET /meters` calls threw "a second operation was started", and Production doesn't validate scopes, so it started anyway. A test confirms the host now refuses to start if this comes back. |
| `Program.cs`, `Infrastructure/HttpBaseAddress.cs` | The base address always ends in `/`. | With the production value `https://meters.example.net/v2`, `meters/X/readings` resolved to `/meters/X/readings`, which drops `v2`. Every vendor call got a 404 and nothing synced. |
| `Application/SyncWorker.cs` | The catch is now `when (!stoppingToken.IsCancellationRequested)`, and sync and sweep have separate try/catch blocks. | An HttpClient timeout is a `TaskCanceledException`. It escaped the old filter, faulted the worker and, with `StopHost`, stopped the pod. A failed sync also skipped the alert sweep. |
| `Program.cs` | `SmsClient` gets a 15 s timeout and deliberately no retry handler. | It had the 100 s default. A retry on a POST is a duplicate SMS. |

### Security

| File | What | Why |
|---|---|---|
| `Api/MeterEndpoints.cs` | `POST /meters` loads the caller's patient record (`sub`) and returns 404 unless `nationalId` matches it. The 404 is the same as for an unknown id. | IDOR: any signed-in patient could link a meter to anyone's national id. The victim would get alerts for the attacker's readings, and 404 vs 201/409 revealed who is a patient. |
| `Api/LinkMeterRequestValidator.cs` | `nationalId` is required, at most 10 characters. The serial is trimmed and required, at most 40. | Neither was validated before; a missing national id reached the query. |
| `Program.cs` | Audience is validated as `meter-sync` (`Audience` plus `ValidAudience`). | `ValidateAudience = false` accepted a token the realm issued to any other client. |
| `Program.cs` | CORS allows only `Cors:Origins`, GET/POST, and the `Authorization` and `Content-Type` headers, with no credentials. | `SetIsOriginAllowed(_ => true)` with `AllowCredentials` answered every origin, and the configured origins were never read. |
| `Api/CurrentPatient.cs` | `TryParse` returns `int?`, and endpoints answer 401. | A missing or non-numeric `sub` threw and returned 500. |
| `Program.cs` | `AddProblemDetails` and `UseExceptionHandler` come first in the pipeline. | Errors now return ProblemDetails, with no detail in Production. |
| `Program.cs` | HSTS runs outside Development. | Required in production by the house rules. |
| `Program.cs`, `Api/SecurityHeadersMiddleware.cs` | Every response gets `nosniff`, `X-Frame-Options: DENY`, a strict CSP and `Cache-Control: no-store`. I moved this before CORS and auth so 401s get the headers too. | The responses carry health data. |
| `Application/MeterSyncService.cs` | Warnings log the vendor id and reason, never the glucose value. | A glucose value in the logs is health data. |
| `appsettings.json` | `System.Net.Http.HttpClient` logs at `Warning`. | Request URLs contain meter serials. The fixed config values are unchanged. |
| `MeterSync.csproj` | Adds `NuGetAudit` with `NuGetAuditMode=all`, and makes `NU1901`–`NU1904` and `CS8600/8602/8603` errors. | The build now fails on any known-vulnerable package, direct or transitive. None today. |

### Schema

| File | What | Why |
|---|---|---|
| `Infrastructure/SyncDbContext.cs` | Foreign keys with `Restrict`: meter→patient, reading→meter, reading→patient. These also index `readings.MeterId` and `PatientId`. | There were none, so orphan rows were possible (the source of the sweeper's `KeyNotFoundException`), and the per-meter lookup scanned the whole table. |
| `Infrastructure/SyncDbContext.cs` | The `(Alert, AlertSentAt)` index is replaced by a partial index `ix_readings_alert_due` on `Id`, filtered to unsent alerts. | It matches the sweeper's query and stays small. |

## Tests (`MeterSync.Tests/`, 104 tests)

**Written test-first, failing before the fix:**
- `ReadingTests`: HI/LO, mmol/L, separators, lengths, readings before the link
- `AlertTextTests`: Gregorian date `02/11/2026 10:58`, the time-zone fallback, HI/LO wording
- `CurrentPatientTests`
- `LinkMeterRequestValidatorTests`
- `PostgresErrorsTests`
- `HttpBaseAddressTests`: the `/v2` base address
- `SecurityHeadersMiddlewareTests`
- `SyncWorkerTests`: a timeout doesn't end the worker; a stopping host does

**Written after the code:**
- `SyncDbContextTests`: offline schema checks for the raw SQL's column names, indexes, FKs and bounded strings
- `ProgramTests`: hosts the app in Production with no database to check health, 401 without a token, CORS, and that start-up fails if the captive DbContext comes back

## Found but not fixed

- **Meter serial ownership is first come, first served.** A patient can link a serial that isn't theirs if nobody has linked it yet, and then gets its readings. Fixing this needs proof of ownership from the vendor (a pairing code), which VENDOR.md doesn't offer.
- **A pre-owned meter's history** is stored under the new patient. It doesn't alert, but it is attributed to them. The product needs to decide whether to drop pre-link readings entirely.
- **A pod crash between the SMS POST and saving `AlertSentAt`** resends that one alert after the 5-minute lease. The gateway has no idempotency key, so this can't be closed from our side. We chose at-least-once over a missed alert.
- **No migration or SQL script exists** for the new columns (`AlertClaimedAt`, `AlertAttempts`, nullable `ValueMgDl`), the FKs or the partial index. The hidden tests build the schema from the model. Production needs a reviewed up/down script, with `CREATE INDEX CONCURRENTLY` and FKs added `NOT VALID` then validated. The app must not call `Migrate()`.
- **National id and phone are stored in plain text.** Encrypting them at rest, with a blind index for the national-id lookup, is a design change, and it touches the `Patient` constructor contract the tests use.
- **No rate limit on `POST /meters`.** Lower risk now that the endpoint only answers about the caller's own id. A per-`sub` limit could also trip the hidden tests.
- **Connection pool size:** `pods × Maximum Pool Size` must fit under PostgreSQL's `max_connections`. That belongs in the production connection string, which isn't in this repo.
- **The vendor API has no "since" parameter**, so every sync downloads each meter's full history, one meter after another. This will slow down as history grows. Raise it with the vendor.
- **The glucose value is sent over SMS.** That is health data over an insecure channel; confirm patient consent.
- **Structure:**
  - `Infrastructure` returns `Api.MeterResponse`.
  - The endpoints and Application services use `SyncDbContext` directly instead of repositories.
  - Config uses `!` and `?? []` instead of validated typed options (`ValidateOnStart`).
  - Several files hold more than one type.

  These are left as they are to keep the change focused.
- Npgsql identity columns are `GENERATED BY DEFAULT`, not `ALWAYS` as the house rule asks. This is minor.

## Package and coverage checks

- `dotnet list package --vulnerable --include-transitive`: none. `--deprecated`: none.
- Coverage was 0% before (no tests). I didn't measure it after; run `/coverage` for numbers. The database paths can only be covered by the integration tests.
