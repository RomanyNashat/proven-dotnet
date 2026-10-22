# HomeReadings: consolidated review

Sources: the known-traps scan, and the code, security and DBA reviews. Duplicates are merged, and each item
lists who raised it (C = code, S = security, D = DBA, K = scan). Paths are relative to `HomeReadings/`.
Status says whether the item was fixed in this pass. "Left" items say why.

## Disagreements settled

- **The "latest" cache.** C and S wanted to key it per patient; D wanted it removed. **Removed.** The query is one
  seek on `(PatientId, CollectedAt)`. A per-pod cache, even keyed per patient, hides a reading the patient has
  just saved through another pod, and a cache key is one more place to leak.
- **Delivery semantics for care-team alerts** (C, D). The care team has no idempotency key, so a duplicate
  pages a nurse twice, and a lost alert means a high reading nobody sees. **Decision:** resend only when the
  alert certainly didn't arrive (no connection, open circuit, a 5xx, 408 or 429). When delivery is unknown
  (a timeout, a connection lost mid-request, a pod that died mid-send) or the care team refused it (any other
  4xx), the alert is set aside with `NeedsFollowUpAt` and an error is logged for a person to check. It is not
  resent.
- **One runner vs claiming rows** (C: SKIP LOCKED; D: advisory lock or SKIP LOCKED). **Transaction-scoped
  advisory lock**, on its own connection, so each alert's state can commit on its own. A session lock would
  survive in the pool (`postgresql-patterns` §11).
- **ValidateScopes/ValidateOnBuild in Production** (K, C, S, D: turn them on). **Not turned on in Production.**
  There is no captive dependency today (all three reviewers checked). In Production, `ValidateScopes` makes
  every resolution of a scoped service from the root provider throw, and integration harnesses do exactly
  that. A test now builds the host in Development, where both checks are on, so CI still catches a captive
  dependency.
- **Severity of the exception leak.** C rated it Critical, S Warning. **Critical**: on this service the message
  carries the meter's text and database internals.

## Critical

| # | Where | What | Raised by | Status |
|---|---|---|---|---|
| 1 | `Application/ReadingNotifier.cs` | An exception in `RunOnceAsync` ended `ExecuteAsync`, and `StopHost` then stopped the whole API. A poison alert crash-looped the pod. | C, S, D | **Fixed.** Every run is caught (`when not OperationCanceledException`) and logged. Alerts are ordered by attempts, so one that keeps failing can't hold the others up. |
| 2 | `Program.cs` resilience, notifier | Duplicate nurse pages: the standard handler retried the POST; the alert was sent and then marked sent; every replica sent the same rows; a shutdown could cancel the "sent" save. | C, D, S | **Fixed.** `DisableForUnsafeHttpMethods()`. One runner (an advisory lock). The alert is marked as being sent, and committed, before its POST. The POST isn't cancelled on shutdown, and the outcome is saved with `CancellationToken.None`. Unknown outcomes go to follow-up. |
| 3 | `Application/ReadingService.cs:14` | `decimal.Parse` with the request culture. "HI"/"LO", `5,6` or Arabic-Indic digits gave a 500, so the most urgent readings never reached the care team. Under en-US, `5,6` would have been read as 56. | C, S, D | **Fixed.** `MeterValue.TryParse`: invariant parsing, Arabic-Indic digits, `,`/`٫` as the decimal point, and HI/LO. HI always alerts. Anything else is a 400. |
| 4 | `Api/LanguageMiddleware.cs` | ar-SA became the formatting culture. On an image without ICU, `new CultureInfo("ar-SA")` (and en-US) throws, so **every** request was a 500. | C, S | **Fixed.** Middleware removed. `x-language` is parsed to a language code (`LanguageHeader`) and only chooses the text. |
| 5 | `Domain/Reading.cs:34` | The alert threshold ignored the unit, so a mg/dL meter alerted on nearly every reading. | C, S, D | **Fixed.** Unit allowlist (`mmol/L`, `mg/dL`). The threshold compares in mmol/L (200 mg/dL = 11.1). The care team gets the value in mmol/L, with `unit`. |
| 6 | `ReadingService.cs:16-23` | Reading and alert were saved in two `SaveChanges` calls, so a high reading could be stored with no alert, ever. | C, D, S | **Fixed.** One `SaveChanges` through the new `CareTeamAlert.Reading` navigation, with an FK. |
| 7 | `ReadingService.cs:14` | The national ID on the clinical alert comes from the request body. | C, S, D | **Partly fixed.** It must be 10 digits, and must match the ID on the patient's earlier readings (400 otherwise). The full fix needs the ID from the identity provider or a patient registry. That is outside this service, and the request shape must stay. |
| 8 | `Application/ReadingQueries.cs:12-16` | IDOR: `GET /readings/{id}` returned any patient's reading. | C, S, D | **Fixed.** Every query goes through `Mine()` (filtered by patient). Someone else's reading is a 404. |
| 9 | `ReadingQueries.cs:32` | One cache entry `"readings:latest"` for all patients. | C, S, D | **Fixed.** The cache was removed (see above). |
| 10 | `ReadingService.cs:25` | `{@Reading}` logged the national ID, value and notes. | C, S, D | **Fixed.** Only the reading id and whether an alert was raised are logged. |
| 11 | `Api/ErrorMiddleware.cs` | `ex.Message` and the inner message went to the client. Client aborts were logged as errors, and the middleware wrote after the response had started. | C, S, D | **Fixed.** `AddProblemDetails` + `UnexpectedExceptionHandler` + `UseExceptionHandler`: a 500 ProblemDetails with a `traceId` and no exception text. |
| 12 | `Infrastructure/PhotoStore.cs:12` | Path traversal through `file.FileName` (relative or absolute). No type or size check. | C, S, D | **Fixed.** The server names the file `{readingId}/{uuidv7}.{ext}`. The type is detected from the content (JPEG, PNG, HEIC, WebP), with a 10 MB cap plus endpoint request and form limits. The row keeps the relative key. |
| 13 | `Infrastructure/ReadingsDbContext.cs` | `Notes` unbounded (`text`), against the DBA's hard ban. | C, S, D | **Fixed.** `varchar(500)`, and the validator uses the same limit. A test checks the DDL has no `text`/`bytea`/unbounded varchar. |
| 14 | `ReadingService.cs:7` | `CollectedAt` as `DateTime`: without `Z` it is `Unspecified`, and Npgsql refuses it for `timestamptz`, so the POST gave a 500 and the reading was lost. | D (C as warning) | **Fixed.** No offset means Riyadh time; any other time is converted to UTC. Missing → 400. Shape unchanged. |

## Warning

| # | Where | What | Raised by | Status |
|---|---|---|---|---|
| 1 | `ReadingQueries.cs:21` | "Today" was the UTC day, so Riyadh patients saw yesterday until 03:00. | C, S, D | **Fixed.** `RiyadhTime.DayInUtc` (Asia/Riyadh, with a fixed +3 fallback when there is no tzdata). |
| 2 | `NewReading` | No validation: lengths, unit, required fields. | C, S | **Fixed.** `NewReadingValidator`: limits match the columns, and errors come back as a 400 ValidationProblem. |
| 3 | `ReadingNotifier.cs:18` | N+1 query per alert; no FK from alert to reading. | C, D | **Fixed.** `Include` in one query; FK with RESTRICT, plus its index. |
| 4 | `Program.cs:31,40` | The DB check ran on `/health/live`, so a DB outage would restart every pod. | C, D | **Fixed.** Live checks nothing; ready checks the DB (tag `ready`). |
| 5 | `Api/CurrentPatient.cs:12` | `int.Parse` on `sub`: a non-numeric `sub` gave a 500. | C, S | **Fixed.** `patient` policy: the group needs a positive-int `sub` (403 otherwise). Reads `sub`, with `MapInboundClaims=false`. |
| 6 | `Program.cs:25-30` | ClockSkew 5 min; no fallback policy. | S, C | **Fixed.** 30 s skew; deny-by-default fallback policy; health endpoints stay `AllowAnonymous`. |
| 7 | `appsettings.json:3` | Password committed; pool not sized. | C, S, D | **Fixed.** Password removed (user secrets locally, `UserSecretsId` added; vault/K8s Secret in production). `Maximum Pool Size=20;Timeout=15;Command Timeout=30`. The pool size still needs the DBA's agreement for the real replica count. |
| 8 | Repo | No migration or script. | D | **Fixed.** `db/001_create_readings_schema.up.sql` / `.down.sql`, generated from the model and reviewed. |
| 9 | `ReadingsDbContext.cs:31` | The `SentAt` index didn't fit the notifier's query. | D | **Fixed.** Partial index `ix_care_team_alerts_waiting` on `"Id" WHERE "SentAt" IS NULL AND "NeedsFollowUpAt" IS NULL`. |
| 10 | Layering | Application depended on `Api.ICurrentPatient`. | C | **Fixed.** `ICurrentPatient` is now in Application. Application still uses `ReadingsDbContext` directly; see Left. |
| 11 | `Program.cs:35-41` | No security headers or HSTS. | S | **Fixed in part.** `no-store`, `nosniff`, `X-Frame-Options: DENY` on every response; HSTS outside Development. ForwardedHeaders: see Left. |
| 12 | `CareTeam:BaseAddress`, `PhotoRoot` | `!` on config: a missing setting surfaced at the first request. | C | **Fixed.** Options with `ValidateOnStart`. |
| 13 | — | No rate limits on POST /readings and the photo upload. | S | **Left.** See below. |
| 14 | — | National ID stored in plain text on every reading. | S | **Left.** See below. |
| 15 | — | No idempotency key on `POST /readings`, so a mobile retry stores the reading twice. | D, C | **Left.** See below. |
| 16 | — | Photo body limits through nginx (1 MB default). | C | **Left** (ingress config is the platform's). The service now caps at 10 MB; the ingress must allow the same. |
| 17 | — | Zero-vulns gate (`NuGetAudit`, NU1901-1904 as errors) not wired, and the scan was not run. | S | **Left.** It needs a repo-level `Directory.Build.props` and an audit against the feed. Restore here reported no vulnerabilities. |

## Suggestion

- Known trap (K): ValidateScopes/ValidateOnBuild. Covered by the Development host test (see above).
- `LatestAsync` tie-break on `Id` (D). **Fixed**, and `TodayAsync` too.
- snake_case column names (D). **Left**: it's a naming choice to agree with the DBA before the first script runs.
- Notifier runs at start instead of after the first minute (C). **Left**: kept the current timing.
- `builder.Logging.AddSerilog` instead of `Services.AddSerilog` (S). **Left**: no redaction layer exists yet.
- Photos on a pod-local volume; object storage with encryption (C, S, D). **Left**: platform decision.
- `EnableRetryOnFailure` (D). **Left on purpose**: retrying the reading INSERT after a lost commit acknowledgement would duplicate it, and the notifier's own transaction would need the execution strategy.

## Decisions left to others

- **National ID source** (identity provider claim or patient registry): the identity and registry teams.
- **Care team:** accept the alert id as an idempotency key; that would let timeouts be retried safely.
  Confirm whether a 500 can mean "accepted", since it is retried today. Agree the new `unit` and
  `outOfRange` fields; a HI reading is sent with `value: null`.
- **Clinical:** whether LO and low numbers (hypoglycaemia) should alert too. Today only high readings alert,
  as the original rule did.
- **Follow-up of `NeedsFollowUpAt` alerts:** who watches the error log or the column.
- **Rate limits** (per patient, on POST and the upload), with numbers agreed with the product.
- **Encryption of the national ID at rest** (key management).
