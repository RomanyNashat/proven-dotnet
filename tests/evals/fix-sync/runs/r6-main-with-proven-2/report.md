# MeterSync review: what was fixed and what's still open

How it was done:
- `/full-review`: the known-traps scan, a dependency check (no vulnerable or deprecated packages), and code, security and DBA reviewers run in parallel.
- tdd-guide then applied the findings test-first.
- `MeterSync/` builds with 0 errors and 0 warnings. `MeterSync.Tests/` has 115 tests and all pass.

**Nothing has run against PostgreSQL yet** (there's none here). The tests check the SQL text and parameters only. The real database is first exercised by the integration tests: the insert, the claim and mark updates, the 409 path, and two pods at once.

## Fixed

### Patient safety (alerts)
| File | What | Why |
|---|---|---|
| `Domain/Reading.cs`, `Application/MeterSyncService.cs` | `HI` and `LO` are now stored and raise a High or Low alert. `ValueMgDl` is null for them. | Before, they failed to parse and were skipped, so the most dangerous readings (over 600 or under 20 mg/dL) never sent an alert. |
| `Domain/Reading.cs` | `mmol/L` is converted to mg/dL (×18) before the thresholds are applied. Unknown units are rejected. | 5.5 mmol/L (normal) was sent as a "low" alert, and 15 mmol/L (270 mg/dL) sent no alert. |
| `Domain/Reading.cs` | Vendor data is checked: missing or over-length fields and values outside the `numeric(6,1)` range are skipped with a reason. | One bad row used to fail the whole sync on every run. |
| `Application/AlertSweeper.cs`, `Domain/Reading.cs` | Readings taken more than 12 hours ago are stored but not texted (`Reading.AlertWindow`). | The vendor returns a meter's whole history, so the first sync of a new meter would have texted every old reading. |
| `Application/AlertText.cs` | The date is now Gregorian with Latin digits and labelled as Riyadh time. The time zone is looked up once, with a fixed UTC+3 fallback. HI and LO are worded in Arabic. | `ar-SA` takes its calendar from the machine's ICU, so the date was Hijri on some images. A missing tzdata used to throw on every sweep, so no alerts went out. The SMS said "HI" to an Arabic reader. |

### Several pods
| File | What | Why |
|---|---|---|
| `Application/AlertSweeper.cs`, `Domain/Reading.cs`, `Infrastructure/SyncDbContext.cs` | Each reading is claimed with an atomic `ExecuteUpdateAsync` lease (`AlertClaimedUntil`, `AlertAttempts`) before its SMS goes out. The "sent" mark is saved after the send with `CancellationToken.None`. | Every pod used to send every alert, and the gateway has no idempotency key. Sending before saving also re-sent after a failed save. |
| `Application/AlertSweeper.cs` | Each reading has its own try/catch. A failure lets the lease expire and is retried; after 5 attempts the alert is parked. A missing phone is logged, not thrown. | Before, one bad number or missing patient (`KeyNotFoundException`) stopped the batch. The same reading came first every run, so it blocked every alert behind it forever. |
| `Infrastructure/SyncDbContext.cs`, `Application/MeterSyncService.cs` | Inserts use `INSERT … WHERE NOT EXISTS … ON CONFLICT ("VendorId") DO NOTHING` (parameterized), and each meter has its own try/catch. | Two pods inserting the same reading hit the unique index, and the single `SaveChanges` lost every meter's readings. The query that loaded every known vendor id for each meter, every minute, on a column with no index, is gone. `NOT EXISTS` stops each minute's re-sent readings from using up identity values. |
| `Api/MeterEndpoints.cs`, `Infrastructure/DbErrors.cs` | A unique violation when linking now returns 409. | The check-then-insert race used to return a 500. |
| `Application/SyncWorker.cs` | The catch is now `when (!stoppingToken.IsCancellationRequested)`. Sync and sweep each have their own try block. | An HttpClient timeout is a `TaskCanceledException`: it escaped the loop and stopped the worker, and the host with it. A vendor outage also used to stop the alerts. |

### Security
| File | What | Why |
|---|---|---|
| `Api/MeterEndpoints.cs` | `POST /meters` looks the patient up by the caller's id **and** the national id. A mismatch returns 404. | IDOR: anyone could link a meter to any national id. That let them text someone with their own readings, or receive another patient's glucose alerts. It also told them which national ids exist. |
| `Program.cs` | The audience `meter-sync` is checked. | `ValidateAudience = false` accepted any token from the realm. |
| `Program.cs` | CORS allows only `Cors:Origins`, with no credentials, GET/POST only, and only the Authorization and Content-Type headers. | `SetIsOriginAllowed(_ => true)` with `AllowCredentials` reflected any origin, and the setting was never used. |
| `Api/CurrentPatient.cs` | `TryGetId`: a missing or non-int `sub` returns 401. | `int.Parse` on it used to throw and return a 500. |
| `Api/LinkMeterRequestValidator.cs` | The national id is required, up to 10 characters. The serial must match `^[A-Za-z0-9][A-Za-z0-9._-]{0,39}\z`. | The national id wasn't checked at all. A serial of `..` slipped past `Uri.EscapeDataString` and reached another path on the vendor host. |
| `Program.cs` | `AddProblemDetails` and `UseExceptionHandler` come first. HSTS is on outside Development. Every response gets `nosniff`, `X-Frame-Options: DENY`, `CSP default-src 'none'` and `Referrer-Policy: no-referrer`. | Errors used to be bare 500s, and no security headers were sent. |
| `Application/MeterSyncService.cs`, `appsettings.json` | Log lines carry the vendor id and a reason, never the glucose value. `System.Net.Http.HttpClient` logs at Warning. | Glucose values are health data, and the request URIs carry meter serials. |

### DI, configuration, HTTP
| File | What | Why |
|---|---|---|
| `Program.cs` | `PatientDirectory` is now scoped. `ValidateScopes` and `ValidateOnBuild` are on in every environment. | A singleton held one DbContext for every request, so concurrent `GET /meters` calls failed. Production doesn't validate scopes, so it started anyway. |
| `Infrastructure/BaseAddress.cs`, `HttpServiceOptions.cs`, `Program.cs` | The base address gets a trailing `/`. The options are typed and checked at startup. | With `https://meters.example.net/v2` and no slash, `/v2` was dropped from every vendor call, so sync never worked. |
| `Program.cs` | All configuration is read lazily from the service provider. | That way the host's and the test factory's overrides apply. |
| `Program.cs`, `Infrastructure/SmsClient.cs` | The SMS client has a 15-second timeout and deliberately no retry handler. | The default timeout was 100 seconds. A retried POST would send a duplicate SMS. |
| `Infrastructure/SyncDbContext.cs` | Added restrict FKs (meter→patient, reading→meter, reading→patient), indexes on `MeterId` and `PatientId`, a partial due-alerts index on `TakenAt` (replacing the `(Alert, AlertSentAt)` index), and `UseIdentityAlwaysColumns()`. | Without FKs, orphaned rows were possible. The sweep and sync queries needed indexes. The house rule is `GENERATED ALWAYS`. |

Other changes along the way:
- `AlertSweeper` is now scoped and takes its dependencies directly. It still resolves from a scope.
- `Reading.MarkAlertSent` was removed; nothing used it.

## Found but not fixed
- **Delivery is at-least-once in one case.** If a pod crashes between the gateway accepting the SMS and the "sent" mark, the lease expires and the patient gets the SMS twice. I chose that over a missed alert. It's a product decision, worth an ADR with the care team.
- **12-hour alert window.** The length is my choice; the care team should confirm it.
- **Readings taken before a meter was linked are still imported** under the new patient. On a second-hand meter, they're the previous owner's. Filtering on `LinkedAt` would also drop the patient's own readings from just before linking, so it needs a product decision.
- **National id and phone are stored as plaintext.** PDPL and the house rule want them encrypted, with a keyed hash for the national id lookup. That needs key management (Key Vault).
- **No migrations or reviewed SQL scripts exist.** The schema changes above (new columns, FKs, indexes) need an up/down script pair for production: indexes `CONCURRENTLY`, FKs `NOT VALID` then `VALIDATE`.
- **Connection pool sizing.** Npgsql allows 100 connections per pod and PostgreSQL defaults to `max_connections=100`. Set `Maximum Pool Size` in the production connection string for the number of pods.
- **Sync cost grows with history.** The vendor API has no "since" filter, so every minute every pod fetches each meter's full history and makes one DB round trip per reading. Also not done: a `pg_try_advisory_lock`, which would let only one pod sync each minute.
- **No rate limiting on `POST /meters`.** No HTTPS redirection or forwarded-headers setup either; TLS is assumed to end at the ingress.
- **Serial numbers are compared case-sensitively** (`abc1` and `ABC1` both get in). It's unclear whether the vendor treats them as case-sensitive.
- **Layering.** `PatientDirectory` (Infrastructure) returns the API's `MeterResponse`, and the endpoints use `SyncDbContext` directly. Fixing that is a restructure, not a production blocker.
- **Coverage wasn't measured.** All tests run without a database, so the SQL paths are covered only at the text level.
