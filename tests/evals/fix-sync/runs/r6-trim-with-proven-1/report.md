# MeterSync: review and fixes

**How it was done:**
- `/full-review` ran over `MeterSync/`: the known-traps scan, then the code, security and DBA reviewers in parallel, plus the dependency check.
- The tdd-guide agent then applied the findings test-first.

**Result:**
- The service builds with 0 warnings and 0 errors.
- `MeterSync.Tests/` (xUnit, FluentAssertions 7.x): 130 tests pass. Every test failed against the old code before its fix.
- Dependencies: no vulnerable packages (direct or transitive) and no deprecated ones.
- Coverage of the new tests is 70.5% line and 71.6% branch. That is below the 80% line target. The gap is code that needs PostgreSQL (see "Not proven here").

## Problems fixed

### Wrong or missing alerts (patient safety)
| File | What | Why |
|---|---|---|
| `Domain/Reading.cs`, `Application/MeterSyncService.cs` | `HI` and `LO` readings are now stored. `HI` becomes 600 mg/dL and alerts High; `LO` becomes 20 mg/dL and alerts Low. | `decimal.TryParse` failed on them, so they were skipped. These are the most dangerous readings (over 600 or under 20), and they never alerted. |
| `Domain/Reading.cs` | `mmol/L` values are multiplied by 18 before they are compared with the mg/dL thresholds. Unknown units are rejected. | 15 mmol/L (270 mg/dL, high) was stored as 15 and texted as **low**, and every normal mmol/L reading triggered a "low" SMS. |
| `Domain/Reading.cs` | Numbers parse with `AllowDecimalPoint` only, are rounded to the column's 1 decimal, and are rejected above what `numeric(6,1)` can hold. Fields longer than their column are rejected. | `NumberStyles.Number` accepted thousands separators and signs, and one value too long for its column failed the save on every run. |
| `Application/AlertSweeper.cs`, `Domain/Reading.cs` | Only readings taken within the last 24 hours (`Reading.AlertWindow`) are texted. | The vendor returns a meter's whole history. Linking a meter, or catching up after an outage, would text the patient about every old out-of-range reading, 100 a minute. |
| `Application/AlertText.cs`, `ArabicCulture.cs`, `RiyadhTime.cs` | The date uses an `ar-SA` clone with the Gregorian calendar set explicitly, and falls back to invariant `yyyy-MM-dd` when there is no ICU. The Riyadh zone falls back to a fixed UTC+3 when tzdata is missing. `HI` and `LO` are written as Arabic phrases. | Plain `ar-SA` prints Hijri or Gregorian depending on the machine's ICU: on this machine it printed year 1448. Without tzdata, every alert threw. |

### Several pods
| File | What | Why |
|---|---|---|
| `Application/AlertSweeper.cs`, `Domain/Reading.cs`, `Infrastructure/SyncDbContext.cs` | Each reading is claimed with one conditional `ExecuteUpdate` before it is sent (new `AlertClaimedUntil` lease of 5 minutes and `AlertAttempts`, at most 3). Only the pod that updates the row sends the text. | Every pod picked the same unsent rows, and the gateway has no idempotency key, so N pods meant N texts. |
| `Application/AlertSweeper.cs` | Errors are caught per reading, a missing phone is logged and skipped, and the reading id is logged, never the phone. | One bad number or gateway error threw out of the loop, so that reading came first again every run and blocked every alert behind it. A `KeyNotFoundException` did the same. |
| `Application/MeterSyncService.cs` | Each meter has its own try and its own save. Only the incoming vendor ids are looked up, into a `HashSet`. After a unique violation (another pod got there first), the change tracker is cleared and the meter is retried once. Logs no longer contain glucose values. | One vendor error or one duplicate `VendorId` from another pod lost the whole run for every meter. The per-meter query also loaded the meter's entire history every minute. |
| `Application/MeterLinker.cs`, `Infrastructure/DbUpdateExceptionExtensions.cs` | Two concurrent links of the same serial return 409, not 500 (PostgreSQL `23505`). | It was check-then-insert, so two requests could both pass the check. |

### Security
| File | What | Why |
|---|---|---|
| `Api/MeterEndpoints.cs`, `Application/MeterLinker.cs` | `POST /meters` links only to the caller (`sub`), and only if the `nationalId` is the caller's own. Otherwise it returns 404, the same answer as an unknown id. | IDOR: any patient could link a meter to any national id, which redirected someone else's readings and alerts, and the 404/201 answers revealed which ids exist. |
| `Api/LinkMeterRequestValidator.cs` | `nationalId` must be digits, at most 10. `serialNumber` must match `^[A-Za-z0-9][A-Za-z0-9._-]{0,39}\z`. | There was no national id validation, and a serial of `..` changed the vendor URL's path. |
| `Program.cs` | `PatientDirectory` is now scoped. `ValidateScopes` and `ValidateOnBuild` are on in every environment. | A singleton held a scoped `DbContext`. In Production one context was shared by every request: concurrent `GET /meters` calls failed, and nothing caught it at start-up. |
| `Program.cs` | The audience `meter-sync` is validated. | `ValidateAudience = false` accepted tokens the realm issued to any other app. |
| `Program.cs` | CORS allows only `Cors:Origins`, with no credentials, only GET and POST, and only the Authorization and Content-Type headers. | It reflected every origin, credentials included, and never read the configured list. |
| `Api/CurrentPatient.cs` | `TryGetId` replaces `int.Parse`; a missing or non-numeric `sub` now gets 403. | Before, it was an unhandled 500. |
| `Program.cs`, `Api/SecurityHeaders.cs` | ProblemDetails, `UseExceptionHandler` (no internals), status-code pages, HSTS outside Development, and `nosniff`, `X-Frame-Options: DENY` and a deny-all CSP. | There was no global error handler and no security headers. |

### Reliability and configuration
| File | What | Why |
|---|---|---|
| `Infrastructure/BaseAddress.cs`, `Program.cs` | Base addresses get a trailing `/` in code. The setting itself stays `https://meters.example.net/v2`. | `.../v2` plus `meters/x/readings` resolved to `/meters/x/readings`, which dropped `v2`: every vendor call would have missed. |
| `Program.cs` | `SmsClient` has an explicit 15 s timeout, and still no retry handler. | Every POST is a text, so a retry would send a duplicate SMS. |
| `Application/SyncWorker.cs` | `catch … when (!stoppingToken.IsCancellationRequested)`, and sync and sweep each get their own try. | An HttpClient timeout is a `TaskCanceledException`. It escaped the loop, the worker stopped and, with the default `StopHost`, the pod went down. A vendor outage also stopped alerts. |
| `Infrastructure/SyncDbContext.cs` | Restrict foreign keys readings→meters, readings→patients and meters→patients; an index on `readings(MeterId, TakenAt)`; and a partial index for the sweeper in place of `(Alert, AlertSentAt)`. | There were no FKs, so readings could point at nothing. Nothing indexed the per-meter lookup, and the old index didn't fit the sweeper's query. |

## Not proven here (needs PostgreSQL)
These compile and follow the design, but no test ran them:
- the cross-pod claim race in the sweeper
- the sync retry after a unique violation, and per-meter isolation
- the 404 and 409 paths of `POST /meters`
- whether PostgreSQL accepts the partial-index filter (the test checks the quoted column names against the model)

## Found but not fixed
- **Resend after a crash:** if a pod crashes after the gateway accepted the text but before `AlertSentAt` is written, the alert is sent again after the 5-minute lease. The gateway has no idempotency key, so this can't be closed from this side. A gateway idempotency key, or a person checking expired claims, would.
- **24 h alert window:** this is a product choice to confirm with the care team. Stale out-of-range readings are stored but never texted, and they stay in the partial index.
- **Readings that can't be read** are logged again on every sync, since they are never stored.
- **No migrations or SQL scripts** exist for the schema, including the new columns, FKs and indexes. They need reviewed up and down scripts, indexes built `CONCURRENTLY`, and FKs added `NOT VALID` and then validated.
- **Connection pool:** with no `Maximum Pool Size`, each pod defaults to 100, which can pass PostgreSQL's default `max_connections` with two pods. The connection string is production config, so I left it.
- **Vendor full-history fetch:** each sync downloads every reading a meter ever uploaded, and the standard resilience handler's timeout will eventually trip on large histories. The vendor API documents no "since" parameter. A per-meter watermark would still cut the DB work.
- **National id and phone** are stored in plaintext, against the PII-at-rest rule. That needs key management, and deterministic encryption or a hash for the national id lookup.
- **Layering:** endpoints, application and infrastructure share one project, and `PatientDirectory` (Infrastructure) returns an API DTO.
- **Not added:** a rate limit on `POST /meters`, an audit log of meter linking, and `UseForwardedHeaders` (it needs the ingress's known proxies). I also left out `UseHttpsRedirection`, because TLS terminates at the ingress and the in-process tests use http.
- **csproj:** `NuGetAuditMode=all` with NU1901–NU1904 as errors, and CS8600/8602/8603 as errors, are not set yet.
- **Exception logging:** the worker's error logs pass the whole exception to the logger. Npgsql and HttpClient messages don't carry PII by default, but there is no masking layer.
