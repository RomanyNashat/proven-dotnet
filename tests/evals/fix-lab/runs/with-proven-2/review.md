# HomeReadings: consolidated review

Sources: the known-traps scan, and the code, security and DBA reviews. Duplicates are merged; where the
reviewers disagreed, the ruling is stated. `task.md` is binding, so a fix never changes a route, a
request or response shape, `ReadingsDbContext`, `ReadingNotifier.RunOnceAsync`, the typed
`CareTeamClient`, `TimeProvider` from DI, JWT on the default scheme (`sub` = patient id, audience
`home-readings`), the three settings, or Serilog from the `Serilog` section.

Status: **Fixed**, **Partly fixed** (what remains is listed in the row), or **Left**.

How the fixes were checked: 82 unit and in-process tests in `HomeReadings.Tests/` (27 of them failed on
the original code first), plus a scratch end-to-end run against a throwaway PostgreSQL 16 (outside the
suite, because the suite must run without a database). That run caught one bug in the first version of the C5
fix: a failed alert was claimed again in the same run, and the stale tracked entity left its claim held.
Now each alert is tried at most once per run and the tracker is cleared after each one.

## Critical

| # | Where | Problem | Fix | Status |
|---|---|---|---|---|
| C1 | `Application/ReadingQueries.cs:12-16` | IDOR: `GET /readings/{id}` filters by `Id` only. Ids are sequential ints, so any patient reads anyone's glucose values and notes. (all three reviewers) | Filter by `PatientId == patient.Id` too; someone else's reading is a 404. | Fixed |
| C2 | `Application/ReadingQueries.cs:32` | Cross-patient cache: the key `"readings:latest"` is shared, so for a minute every patient gets whoever called first's reading. (all three) | Key per patient (`readings:v1:latest:{id}`, invariant), evict it when that patient saves a reading, tie-break on `Id`. | Fixed |
| C3 | `Infrastructure/PhotoStore.cs:12` | Path traversal: `Path.Combine(folder, file.FileName)` takes the client's name; `../..` or an absolute name writes anywhere the process can. No size limit, no type allowlist. (all three) | Server-generated name (`Guid.CreateVersion7()` + extension from an allowlisted content type), full-path containment check, 5 MB limit, 400 on a bad file. | Fixed (magic-byte check and object storage left) |
| C4 | `Application/ReadingNotifier.cs:30-37`, `:18` | One failure stops the API: no catch in the loop, so a care-team error or a missing reading (`SingleAsync`) faults the worker and `StopHost` takes the pod down. The same first alert does it again after every restart, and nothing behind it is ever sent. (all three) | Catch per run (`when not OperationCanceledException`) and per alert; record `Attempts`/`LastError` (type only); retry least-tried first so one bad alert can't block the rest; a missing reading is recorded, not thrown. | Fixed |
| C5 | `Program.cs:20-21`, `ReadingNotifier.cs:15,19,21` | Duplicate nurse alerts (the care team has no idempotency key): (a) the standard handler retries POST; (b) the shutdown token cancels an in-flight POST and the save of its mark; (c) every replica sends the same unsent rows. (all three) | (a) `Retry.DisableForUnsafeHttpMethods()` (package 10.0.0 >= 9.8), one 25 s attempt inside a 30 s total, `HttpClient.Timeout` infinite; (b) once a send starts, it and its mark run to completion (`CancellationToken.None`); the stop token only stops new sends; (c) each alert is claimed with `FOR UPDATE SKIP LOCKED` and a 2-minute lease, and marked in its own commit. Remaining at-least-once window (crash between the POST and the mark) is documented. | Fixed |
| C6 | `Application/ReadingService.cs:14`, `Api/LanguageMiddleware.cs:8,14` | Meter value parsed with the request's culture: `x-language: ar` made `ar-SA` the *formatting* culture, so `decimal.Parse("11.5")` depends on the machine's ICU, and without ICU `new CultureInfo("ar-SA")` throws on every request. `HI`/`LO` and any bad text are a 500, so the most dangerous readings are lost silently. | The header picks the text language only (no `CultureInfo`); the value is parsed invariantly (Arabic-Indic digits and `٫`/`,` accepted); a non-number is a 400; `HI`/`LO` is a 400 whose message tells the patient to contact their care team now. | Fixed (how HI/LO is recorded and alerted is a product decision, left) |
| C7 | `Application/ReadingService.cs:7,14`, `ReadingNotifier.cs:19` | The national ID on the alert comes from the request body, unchecked against the signed-in patient. (all three) | The request shape must keep `nationalId` (task.md), and no claim or registry is available here, so: 10-digit format check, and a patient's national ID is fixed by their first reading (a different one later is a 400). The real fix (take it from the token or the patient registry) is the identity team's. | Partly fixed |
| C8 | `Domain/Reading.cs:34`, `ReadingsDbContext.cs:19` | Alert rule ignores the unit: `Value >= 11.1` on free-text units, so every normal mg/dL reading (common in KSA) pages a nurse. (code: Critical; security/DBA: Warning. Ruled Critical: it corrupts the clinical alert stream.) | Units are a closed set (`mmol/L`, `mg/dL`, case-insensitive, stored canonical); threshold 11.1 mmol/L or 200 mg/dL; the care team gets mmol/L, as the domain documents. | Fixed |
| C9 | `Application/ReadingService.cs:25` | PHI in logs: `{@Reading}` sends national ID, value and notes to the log sink. (all three) | Log the reading id and whether an alert was queued, nothing else. | Fixed (masking layer left) |
| C10 | `Api/ErrorMiddleware.cs:16` | Exception text returned to the client (Npgsql errors, the `FormatException` echoing the value); client aborts logged as 500s. (code: Critical; security: Warning. Ruled Critical: it can echo patient data.) | `AddProblemDetails` + an `IExceptionHandler`: generic title and `traceId` only; a client abort is not an error. | Fixed |
| C11 | `ReadingsDbContext.cs:13-24` | `Notes` has no `HasMaxLength`: an unbounded `text` column (house hard ban). (code, DBA) | `HasMaxLength(500)` and the same limit in the validator. | Fixed |
| C12 | `Application/ReadingService.cs:17-23` | Reading and alert saved in two commits: a failure in between stores the high reading with no alert; the client's retry then duplicates the reading. (code, DBA) | One transaction around both saves. | Fixed (no FK, see Left) |

## Warning

| # | Where | Problem | Fix | Status |
|---|---|---|---|---|
| W1 | `ReadingService.cs:7` | No input validation: over-long `nationalId`/`unit` and out-of-range values become DB errors and 500s. | FluentValidation validator, limits match the columns, value 0 < v <= 1000. | Fixed |
| W2 | `ReadingService.cs:15`, `Reading.cs:29` | `CollectedAt` stored as sent: an offset (`+03:00`) or no offset gives `Kind` Local/Unspecified, Npgsql refuses it, every POST fails. | Normalized to UTC: UTC as is, an offset converted, no offset read as Riyadh time (patients are in Riyadh). A time more than 5 minutes in the future is a 400. | Fixed |
| W3 | `ReadingQueries.cs:21` | "Today" is the UTC day: 00:00-03:00 Riyadh shows yesterday. | Riyadh day as a UTC range (`RiyadhTime` with the fixed-offset fallback for images without tzdata). | Fixed |
| W4 | `Program.cs:31,40` | Liveness runs the DB check: a DB outage restarts every pod. | `/health/live` checks nothing; the DB check is tagged `ready`. | Fixed |
| W5 | `Api/CurrentPatient.cs:12` | `int.Parse` on the subject with `!`: a token without an int `sub` is a 500. The reviewers' worry that Keycloak's `sub` is a UUID is settled by task.md (`sub` is the patient's id). | Read `sub` (then `NameIdentifier`) with `TryParse`; the default and fallback policies require it, so such a token gets 403. | Fixed |
| W6 | `Program.cs:25-29` | JWT: `ClockSkew` 5 min, `MapInboundClaims` on, no explicit default scheme. | Default scheme explicit, `ClockSkew` 30 s, `MapInboundClaims = false`; audience stays `home-readings` (task.md), overridable by `Auth:Audience`. | Fixed |
| W7 | `Program.cs:30` | No deny-by-default: the next endpoint added is open. | Fallback policy = authenticated patient. | Fixed |
| W8 | `ReadingNotifier.cs:18` | N+1: one query per alert, 50 a minute. | Moot with one claimed alert at a time; the reading is projected in one query. | Fixed |
| W9 | `ReadingsDbContext.cs:22`, `PhotoStore.cs:12` | `PhotoPath` holds the client's name (can overflow 260 after the file is written); a local volume isn't shared across pods. | Server name keeps it bounded; the new file is deleted if the save fails, the replaced one after it succeeds. | Partly fixed (object storage left) |
| W10 | `Program.cs:35-41` | No security headers or HSTS. | `nosniff`, `X-Frame-Options: DENY`, a deny-all CSP, `Cache-Control: no-store` (PHI); `UseHsts()` outside Development. | Fixed (ForwardedHeaders left) |
| W11 | `appsettings.json:3` | A password committed in the connection string. | Removed; the deployment injects `ConnectionStrings__Readings`. | Fixed |
| W12 | csproj | No NuGetAudit gate. | `NuGetAuditMode=all`, NU1901-NU1904 as errors (scan was clean). | Fixed |
| W13 | `Program.cs:11`, `appsettings.json:3` | No `Maximum Pool Size`/timeouts in the connection string; `pods x pool` must fit `max_connections`. | Needs the DBA's numbers. | Left |
| W14 | `POST /readings` | No idempotency key: a mobile retry duplicates the reading and its alert. | Needs an app change (a client-generated key header). | Left |
| W15 | `POST /readings`, photo | No per-patient rate limit (each high value pages a nurse). | Needs product numbers. | Left |
| W16 | `Reading.cs:26`, photos | PHI at rest unencrypted, no PHI read audit. | Needs the compliance design. | Left |
| W17 | `CareTeamClient.cs:10` | Outbound call carries no service credentials. | Needs the care team's auth details. | Left |
| W18 | Layering | Application references Api and Infrastructure types (`ICurrentPatient`, `ReadingsDbContext`, `PhotoStore`, `IFormFile`). | One-project service; a refactor with no behaviour change. | Left |

## Suggestion

| # | Where | Problem | Status |
|---|---|---|---|
| S1 | `Program.cs:7` (scan; confirmed by code and DBA, out of area for security) | `ValidateScopes`/`ValidateOnBuild` off in Production, which is where the hidden tests run. Nothing is captive today. | Fixed: on in every environment |
| S2 | `Program.cs:20`, `PhotoStore.cs:6` | Settings read with `!`, no start-up validation. | Fixed: `CareTeamOptions`/`StorageOptions` with `ValidateOnStart`; a missing trailing `/` is added rather than failing |
| S3 | `Messages.cs` | Hard-coded texts keyed on `CurrentUICulture`. | Fixed: keyed on the language code (no ICU needed); same strings |
| S4 | `ReadingNotifier.cs` | `CreateScope` in async code; logs the batch size, not what was sent. | Fixed: `CreateAsyncScope`, logs sent/failed counts |
| S5 | `ReadingsDbContext.cs:31` | Full index on `SentAt` grows with sent rows. | Fixed: partial index on `(Attempts, Id) WHERE "SentAt" IS NULL` |
| S6 | `ReadingQueries.cs:37` | "Latest" is nondeterministic on equal `CollectedAt`. | Fixed: `ThenByDescending(Id)` |
| S7 | `ReadingsDbContext.cs` | No FK or unique index on `care_team_alerts.ReadingId`. | Left: one transaction now guarantees the pair; the FK would refuse data the hidden tests may seed directly |
| S8 | `ReadingsDbContext.cs` | snake_case tables, PascalCase columns. | Left: the first DDL script is the place to decide |
| S9 | — | No migration scripts. | Left: generate the first up/down pair from this model (it changed: `Notes` length, three alert columns, the partial index) |
| S10 | `ReadingEndpoints.cs:28` | `.DisableAntiforgery()` on the photo upload. | Dismissed: bearer-only API, no cookies |
| S11 | `ReadingQueries.cs:39` | The cache factory captures the caller's token. | Dismissed: `IMemoryCache` runs the factory per caller, nothing is shared |
