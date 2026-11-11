# MeterSync: review and fixes

Found by `/full-review`: a known-traps scan plus code, security and database reviewers. Fixed test-first by the tdd-guide agent.
The service builds with 0 warnings and 0 errors. `MeterSync.Tests/` passes 81 of 81. Nothing here ran against PostgreSQL, so the raw SQL (row locks, advisory locks) has been reasoned through but not run.

## Fixed

### Readings never synced, or were stored wrong
| File | What | Why |
|---|---|---|
| `Program.cs`, `Infrastructure/BaseAddress.cs` | A trailing `/` is added to the base addresses in code. The setting stays `https://meters.example.net/v2`. | Without it, `meters/{sn}/readings` resolved to `https://meters.example.net/meters/...` and dropped `/v2`. Every vendor call got a 404 and no reading ever synced. |
| `Domain/Reading.cs` (`FromVendor`) | `HI` is stored as 600 mg/dL with a High alert. `LO` is stored as 20 with a Low alert. `Raw` keeps what the meter showed. | These were skipped as "not a number". The most dangerous readings (over 600, under 20) never raised an alert. |
| `Domain/Reading.cs` | `mmol/L` values are multiplied by 18. Any unit other than mg/dL or mmol/L is rejected. Units are compared case-insensitively. | mmol/L values were compared with mg/dL thresholds, so every normal mmol/L reading raised a "low" SMS, and 15 mmol/L (270 mg/dL) raised a "low" instead of a "high". |
| `Domain/Reading.cs` | Numbers are parsed with `AllowDecimalPoint` only. A value, id or unit longer than its column is skipped. | `NumberStyles.Number` accepts thousands separators. One value too long for its column failed the single `SaveChanges` and dropped every meter's readings. |
| `Application/MeterSyncService.cs` | Each meter is fetched in its own try/catch, then saved in its own transaction. | One meter's vendor error (a mistyped serial, a timeout) threw before anything was saved, so no meter synced. |
| `Application/MeterSyncService.cs` | `pg_advisory_xact_lock(namespace, meterId)` is taken before the known-ids check. The check only looks at this batch's ids, through the unique index. A unique violation is caught as a safety net. | Two pods running check-then-insert hit the unique `VendorId` index and lost the whole batch every minute. The old check also loaded the meter's whole history and scanned it with `List.Contains`. |
| `Application/MeterSyncService.cs` | An unusable reading logs only its vendor id. | The log line used to include the glucose value, which is health data. |

### Alerts: double-sent, blocked, or wrongly formatted
| File | What | Why |
|---|---|---|
| `Application/AlertSweeper.cs` | Each reading is claimed with `SELECT … FOR UPDATE SKIP LOCKED` in its own transaction, then sent, then marked sent and committed. | Every pod read the same unsent rows and texted them. The gateway has no idempotency key, so the patient got one SMS per pod. |
| `Application/AlertSweeper.cs`, `Domain/Reading.cs`, `SyncDbContext.cs` | Failures are handled per reading: rolled back, logged by reading id and error type only, and counted in a new `AlertFailures` column. A reading stops being retried after 5 failures, and a missing phone counts as a failure. `RunOnceAsync` returns the number actually sent. | One failing SMS (a bad phone gets a 400) or a missing patient (`KeyNotFoundException`) aborted the sweep. Since rows are taken in `Id` order, the same row came first every minute and no patient got an alert again. |
| `Application/AlertText.cs` | The time is formatted with InvariantCulture as Gregorian `dd/MM/yyyy HH:mm`. The Riyadh zone is cached, with a fixed UTC+3 fallback (Saudi Arabia has no daylight saving). | `ar-SA` takes its calendar from the machine's ICU, so one image printed the Hijri year (1448) and another the Gregorian. On an image without tzdata, `FindSystemTimeZoneById` threw for every alert. |
| `Program.cs` | `SmsClient` has a 10 s timeout and deliberately no retry handler. | The default timeout was 100 s, and retrying a POST to a gateway with no idempotency key sends duplicate texts. |
| `Application/SyncWorker.cs` | The filter is now `catch … when (!stoppingToken.IsCancellationRequested)`. Sync and sweep each have their own scope and try block. | An HttpClient timeout throws `TaskCanceledException`, which escaped the old filter and stopped the worker and the host. A failed sync also skipped the sweep. |

### Security
| File | What | Why |
|---|---|---|
| `Api/MeterEndpoints.cs` | `POST /meters` links only to the caller's own patient record (`sub`). Someone else's national id gets the same 404 as an unknown one. | IDOR: any signed-in user could link a meter to any national id, putting their readings into the victim's record and their alerts on the victim's phone. The 404 vs 201/409 answers also showed which national ids were registered. |
| `Api/MeterEndpoints.cs` | `nationalId` is validated (required, at most 10 characters). A unique violation on the serial returns 409. | No input validation. Two simultaneous links of one serial returned a 500. |
| `Api/CurrentPatient.cs`, `Api/MeterEndpoints.cs` | `sub` is read with `TryParse`. A missing or non-integer `sub` gets a 403. | `int.Parse` returned a 500. |
| `Program.cs` | The audience `meter-sync` is validated, and `RequireHttpsMetadata` is set. | With `ValidateAudience = false`, a token issued in the realm for any other client was accepted for health data. |
| `Program.cs` | CORS allows only the origins in `Cors:Origins`, with GET/POST and the `Authorization`/`Content-Type` headers, and no credentials. Startup fails if the list is empty. | `SetIsOriginAllowed(_ => true)` with `AllowCredentials()` reflected every origin, and the configured list was never read. |
| `Program.cs`, `Api/SecurityHeaders.cs` | `AddProblemDetails` and `UseExceptionHandler`, HSTS outside Development, and the `nosniff`, `X-Frame-Options: DENY` and CSP headers on every response. | There was no global error handler and there were no security headers. |

### DI and database
| File | What | Why |
|---|---|---|
| `Program.cs` | `PatientDirectory` (and `AlertSweeper`) are scoped. `ValidateScopes` and `ValidateOnBuild` are on in every environment. | The singleton `PatientDirectory` captured one `DbContext` for the whole app, so concurrent `GET /meters` threw "A second operation was started". Production skips scope validation, so this was never caught. |
| `Infrastructure/SyncDbContext.cs` | Three foreign keys, all `Restrict`: meter→patient, reading→meter, reading→patient. | Orphan rows were allowed, and an orphan reading crashed the sweep. |
| `Infrastructure/SyncDbContext.cs` | The `(Alert, AlertSentAt)` index is replaced by a partial index on `Id` with `WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL`. | The old index fits `<> 'None'` poorly. The new one matches the sweeper's query exactly. |
| `Infrastructure/SyncDbContext.cs` | `UseIdentityAlwaysColumns()`, and the column lengths come from the `Reading` constants. | House rule: `GENERATED ALWAYS AS IDENTITY`. The validation and the columns now share one limit. |

### Tests added (`MeterSync.Tests/`)
- `ReadingTests`: HI, LO, mmol/L, unknown units, length limits, thresholds.
- `AlertTextTests`: the Gregorian date in Riyadh time, and the zone fallback.
- `BaseAddressTests`.
- `CurrentPatientTests`.
- `ProgramTests` (in-process host, Production, locally signed tokens, no database):
  - security headers;
  - 401 for a token with the wrong audience;
  - 403 for a bad `sub`;
  - 400 for invalid bodies;
  - CORS allowlist;
  - SMS timeout and vendor base address;
  - DI resolution.
- `SyncDbContextTests`: foreign keys, the partial index, `AlertFailures`, identity columns, bounded strings, and the column names the raw SQL uses.
- `SyncWorkerTests`: a timeout neither stops the worker nor skips the sweep.

## Found, not fixed
- **Alerts for old readings.** The vendor returns a meter's whole history, so a newly linked meter texts the patient about every past out-of-range reading (up to 100 a minute). Fixing it means only alerting on readings after `LinkedAt` or within a recent window. The window is a decision for the care team, and the hidden tests' clock is unknown, so I left it unchanged.
- **Claiming someone else's meter.** A patient can link any unlinked serial and receive that device's readings, because the vendor offers no proof of possession. This needs a vendor-side pairing code.
- **Serial case and whitespace.** `ABC1` and `abc1` can be linked as two meters. Normalising would change what `GET /meters` returns, so I left it. The unique-violation catch in sync now stops it from breaking the batch.
- **PII at rest.** National id and phone are stored in plaintext, against PDPL and the house rule. The fix is AES-GCM for the phone and an HMAC blind index for the national id, with keys from a vault. It changes the schema and the lookup, so it needs its own change.
- **SMS after a crash.** A crash after the gateway accepted a message but before the commit sends it again once (at-least-once). I chose this on purpose: a duplicate is better than a lost hypo/hyper alert. A send that succeeds but whose commit fails is also counted as a failure and resent.
- **No migrations.** There are no migration scripts. The schema (including the new foreign keys, partial index and `AlertFailures` column) has to ship as a reviewed up/down script pair, with `CREATE INDEX CONCURRENTLY` and FKs added `NOT VALID` and then validated if the tables already hold data.
- **Connection pool.** `Maximum Pool Size` isn't set. The default of 100 per pod, times the number of pods, exceeds PostgreSQL's default `max_connections` of 100.
- **No rate limiting** on `POST /meters`. **No readiness probe**: `/health/live` doesn't check the database.
- **Vendor history every minute.** The vendor API returns the full history on every poll. The traffic grows over time and needs a vendor "since" parameter.
- **Phone numbers aren't normalised** to `+966…` before sending.
- **Layering.** `PatientDirectory` (Infrastructure) returns the API's `MeterResponse`, and the endpoints use `SyncDbContext` directly. `Created("/meters")` doesn't point at the new resource.
- **Dependencies.** `dotnet list package --vulnerable --include-transitive` found no vulnerable or deprecated packages. Coverage wasn't measured; there were no tests before this change.
