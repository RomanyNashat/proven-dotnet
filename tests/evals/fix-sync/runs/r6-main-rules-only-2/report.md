# MeterSync review

The service builds with 0 warnings. `MeterSync.Tests/` has 35 unit and in-process tests (xUnit, FluentAssertions pinned to `[7.2.0,8.0.0)`), and they all pass. None of them needs PostgreSQL, so **nothing that talks to the database was run here**: the alert claiming, the unique-violation handling and the FKs are checked only as far as model building, DDL generation and LINQ translation go.

## Fixed

### Safety and correctness of readings and alerts

| File | What | Why |
|---|---|---|
| `Application/MeterSyncService.cs`, `Domain/Glucose.cs` (new) | `mmol/L` readings are converted to mg/dL (×18). | The value was stored as mg/dL whatever the unit. A normal 5.5 mmol/L (99 mg/dL) was read as 5.5 mg/dL and texted as a **low**, and a dangerous 15 mmol/L (270) was never alerted. |
| same | `HI` and `LO` are kept and alerted as High and Low. `ValueMgDl` is now nullable for them. | They failed the number parse and were **dropped**, yet they are the most dangerous readings (over 600 / under 20 mg/dL). |
| same | Parsing uses `NumberStyles.AllowDecimalPoint` instead of `Number`. It rejects ≤ 0, unknown units, implausible values, and values or ids too long for their columns. | `Number` accepts thousands separators, so `"5,5"` parsed as 55. Over-long vendor values made the whole save fail. |
| `Domain/Reading.cs` | Only readings taken within the last 12 hours get an SMS. Older out-of-range ones are stored as `Stale`. | The vendor returns a meter's **entire history**, so linking a meter texted the patient about every old high or low it had ever recorded. |
| `Application/AlertSweeper.cs`, `Domain/Reading.cs` | Each alert is claimed with a conditional `ExecuteUpdate` on a 2-minute lease (`AlertState` + `AlertClaimedUntil`) before it is sent. Only the pod whose update matched sends it. The sent state is written right after the send, ignoring cancellation. | With several pods every pod sent every alert. The gateway has no idempotency key, so the patient got one SMS per pod. |
| `Application/AlertSweeper.cs` | A 4xx from the gateway (other than 408/429) marks the reading `Rejected` and the run moves on. 5xx, timeouts and network errors release the claim and end the run. | Before, one failed send threw away the rest of the batch, and a permanently bad number blocked the queue forever. |
| `Application/MeterSyncService.cs` | Each meter is fetched and saved on its own, and a vendor error or bad JSON skips only that meter. A unique violation on `VendorId` (another pod stored the readings first) is caught. Known readings are looked up by vendor id across all meters (ids are vendor-unique), and duplicate ids within one response are removed. | One failing meter used to lose every reading collected in the run. Two pods syncing at once crashed on the unique index. |
| `Application/SyncWorker.cs` | Exceptions are filtered on `stoppingToken.IsCancellationRequested`, and sync and sweep are wrapped separately. | An HttpClient timeout is a `TaskCanceledException`. It got past the `is not OperationCanceledException` filter, ended `ExecuteAsync`, and .NET then **stops the host**. A failing sync also stopped pending alerts from going out. |
| `Application/AlertText.cs` | Dates use the invariant (Gregorian) culture, marked `م`, plus "بتوقيت الرياض". If tzdata is missing, a fixed UTC+3 zone is used (Riyadh has no DST). HI/LO are written out ("أعلى من 600 mg/dL"). | `ar-SA` takes its calendar from the machine's ICU. That is Umm al-Qura (Hijri) on some images, so one pod printed 1448 and another 2026, with no calendar named. `new CultureInfo("ar-SA")` also throws on invariant-globalization images. |
| `Program.cs`, `Infrastructure/ServiceAddress.cs` (new) | A trailing slash is added to the vendor and SMS base addresses. | The configured `https://meters.example.net/v2` has no trailing slash, so `meters/x/readings` resolved to `https://meters.example.net/meters/x/readings`. That **drops `/v2`**, so no request reached the documented API. |
| `Program.cs` | The SMS client has a 15 s timeout and a comment saying why it has no retry handler. | The default timeout is 100 s. A retry handler added later would cause duplicate SMS. The claim lease (2 min) is longer than the timeout. |

### Security

| File | What | Why |
|---|---|---|
| `Api/MeterEndpoints.cs` | `POST /meters` only links to the patient whose `NationalId` matches **and** whose id is the token's `sub`. Anything else gets 404. | Any logged-in patient could attach a meter to any other patient by national id, or attach someone else's meter to themselves and receive that person's glucose alerts (IDOR). The 404/409 difference also let callers check which national ids are registered. |
| `Program.cs` | The JWT audience is now checked (`meter-sync`). | `ValidateAudience = false` accepted any token from the realm, including tokens issued for other apps. |
| `Program.cs` | CORS allows only `Cors:Origins`. `AllowCredentials` is removed. | `SetIsOriginAllowed(_ => true)` together with `AllowCredentials` allowed every website, with credentials. The API uses bearer tokens, so credentials aren't needed. |
| `Program.cs` | Registered `PatientDirectory` as **scoped** instead of singleton. | It holds the scoped DbContext. As a singleton (scope validation is off in Production) all requests shared one non-thread-safe DbContext. That breaks under concurrent requests and keeps stale tracked data. |
| `Program.cs` | Added ProblemDetails, the exception handler, status-code pages, HSTS outside Development, and `nosniff` / `X-Frame-Options` / CSP headers. Missing base addresses now give a clear error. | Errors now come back as RFC 7807 responses with no internal details. |
| `Api/CurrentPatient.cs`, `Api/MeterEndpoints.cs` | A non-numeric `sub` returns 401 instead of a 500 from `int.Parse`. Added `NationalId` validation. `POST` returns 409 instead of 500 when two requests (or two pods) link the same serial at once. | Bad input gave 500 errors. |
| `MeterSyncService.cs` | The log line for a skipped reading no longer includes its value. | A glucose value is a health record, and the rule is not to log health records. |

### Schema and build

| File | What | Why |
|---|---|---|
| `Infrastructure/SyncDbContext.cs` | Added FKs from meters to patients and from readings to patients and meters (restrict). Keys use `GENERATED ALWAYS AS IDENTITY`. Added the new columns, bounded, plus an index on `AlertState`. The length constants are shared with validation. | The tables had no referential integrity, so the sweeper could hit a missing phone (`KeyNotFoundException`). |
| `MeterSync.csproj` | `NuGetAudit` with mode `all`; `NU1901–NU1904` and nullable warnings are treated as errors. | Fails the build on any vulnerable package. There are none today: `dotnet list package --vulnerable --include-transitive` is clean. |

## Found but not fixed

- **Meter ownership proof.** Whoever links a serial number first owns it, and serials are printed on the device. Someone holding another patient's meter could claim it. A real fix needs proof from the vendor (a pairing code, say), which VENDOR.md doesn't offer.
- **Second-hand meters.** History imported on linking may hold a previous owner's readings. They are stored, not texted. Not importing readings from before the link would lose a new owner's own early readings. This is a product decision.
- **Duplicate SMS in rare cases.** If a pod dies after the gateway accepts a message but before `Sent` is written, or a send times out after the gateway sent it, the alert is resent once its lease or retry comes round. The gateway has no idempotency key, so exactly-once delivery isn't possible.
- **Lease times come from each pod's clock.** Clock drift between pods larger than the 2-minute lease could cause a duplicate send. NTP is assumed.
- **Every pod calls the vendor for every meter every minute.** The results are correct but the load is doubled. A PostgreSQL advisory lock or a single CronJob would fix it. Not done, so the two-pod test setup stays simple.
- **No migrations.** The tests build the schema from the model. Production needs reviewed up/down SQL scripts for the new columns and FKs (the app must not migrate itself). `UseIdentityAlwaysColumn` means rows can't be inserted with an explicit id.
- **The patient's phone and national id are stored in plain text.** PII should be encrypted at rest. That needs a key-management decision.
- **One SMS per reading.** A burst of highs sends several texts. Throttling per patient is a care-team decision.
- **Layering.** `PatientDirectory` (Infrastructure) returns the API's `MeterResponse`, and the endpoint uses `SyncDbContext` directly. Moving this into Application handlers is a refactor with no behaviour change, so it was left out.
- **Platform defaults.** There is no rate limiting, no readiness probe that checks the database, and no `UseHttpsRedirection` (TLS is assumed to end at the ingress, and HSTS is sent).
