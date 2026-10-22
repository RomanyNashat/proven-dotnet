# HomeReadings: consolidated review

Sources: the known-traps scan (`scan.md`), and the code, security and DBA reviews. Duplicates are merged;
each item names who raised it (C = code, S = security, D = DBA, K = scan). Line numbers are from the
code as reviewed. Verdict: **not ready for production**. Patients can read each other's readings, nurse
alerts can be duplicated or lost, and an upload can write anywhere on the pod.

## Disagreements settled

1. **Alerts after an unclear send (C5 vs D3).** The care team has no idempotency key, so a send whose
   outcome is unknown (timeout, crash after the POST) can't be made exactly-once from our side. C proposed
   parking such alerts as "Unknown" for a person to check; D proposed keeping at-least-once and narrowing
   the window. **Settled: at-least-once.** A missed alert on a high glucose reading can hurt a patient; a
   duplicate carries the same national ID, value and time, so a nurse can see it is the same reading.
   Everything that *can* be closed is closed: no POST retries in Polly, one sender across pods, the "sent"
   mark committed straight after each send, the reading and its alert saved together. The remaining
   crash window is written down, and the fix for it (an idempotency key, or dedupe on reading + time) is
   the care team's to provide.
2. **Several pods sending (C4, S10, D3).** Row claims (`FOR UPDATE SKIP LOCKED` + new columns) or one
   sender at a time. **Settled: one sender, a PostgreSQL session advisory lock.** Order doesn't matter, but
   a single sender needs no claim columns and no lease expiry, and the lock is released by the database if
   the pod dies.
3. **The "latest" cache (C2, S2, D1).** All three proposed a per-patient key plus invalidation.
   **Settled: remove the cache.** A per-patient entry for an app-start call almost never hits within a
   minute, the query is one row off the `(PatientId, CollectedAt)` index, and an in-memory cache on several
   pods would still show a patient a stale "latest" right after they sent a reading.
4. **The audience from configuration (S7).** Not done: `task.md` fixes the audience as `home-readings`;
   it's the API's own identity, not an environment setting.
5. **Scope validation (K, all three: info).** Kept at info and fixed: it guards future changes; no captive
   dependency exists today.

## Critical

| # | Where | What | Fix | Raised by / skill |
|---|---|---|---|---|
| 1 | `Application/ReadingQueries.cs:12-16` | IDOR: `GET /readings/{id}` filters by id only; ids are sequential | Filter by `PatientId` too; someone else's reading is 404 | C1 S1 D2 / `api-design` §1, rules/security |
| 2 | `ReadingQueries.cs:32` | Global cache key `readings:latest`: every patient gets the first caller's reading for a minute; `null` cached; never cleared | Remove the cache (settled 3) | C2 S2 D1 / `caching` |
| 3 | `Infrastructure/PhotoStore.cs:12`, `Api/ReadingEndpoints.cs:26` | Path traversal: client file name in `Path.Combine` (`../`, or a rooted name drops the root) can overwrite app binaries; no size limit or type allowlist | Server-made name `{readingId}/{guid}.{ext}`, containment check, content-type allowlist, size limit on the endpoint and the file; store a relative key, not the absolute path | C10 S3 D-W8 / rules/security |
| 4 | `Application/ReadingService.cs:7,14` | `NationalId` from the body: a patient can file a reading (and a nurse alert) under someone else's ID | No patient registry here to look it up, and the body shape is fixed by `task.md`. Safe choice: validate the format, and pin it to the patient: a later reading with a different ID than the patient's earlier readings is refused (400). The real fix, the ID from the identity provider or a patient registry, is listed under *Left* | C9 S4 D5 / `owasp-aspnetcore` |
| 5 | `ReadingService.cs:14`, `Api/LanguageMiddleware.cs:14` | `decimal.Parse` under the request culture: `ar-SA` (Arabic separators) can fail `12.5`; under `en-US`, `12,5` becomes 125 (false alert); `HI` throws a 500 and the most dangerous reading is lost | Header sets the text language only; parse with `InvariantCulture`, accept Arabic-Indic digits and `٫`, refuse `,` (ambiguous); map `HI`/`LO` (HI alerts); anything else is a 400 | C8 S9 D7 / `localization` §1, rules/csharp-standards |
| 6 | `Program.cs:20-21` | `AddStandardResilienceHandler()` retries POST: a timed-out alert that arrived is sent again | `Retry.DisableForUnsafeHttpMethods()` (package 10.0.0 ≥ 9.8.0) | C3 S10 D3 / `polly-resilience` |
| 7 | `Application/ReadingNotifier.cs:15` | Every pod sends the same 50 alerts | One sender: session advisory lock (settled 2) | C4 S10 D3 / `outbox`, `worker-patterns` |
| 8 | `ReadingNotifier.cs:33-36` | No try/catch: one care-team failure ends `ExecuteAsync`, `StopHost` takes the API down, and it crash-loops; one bad alert blocks all later ones | Catch per run (not `OperationCanceledException`); per alert record attempts and the exception *type*; a 4xx for one alert skips to the next, an outage stops the batch until the next run | C6 S10 D3 / `worker-patterns` |
| 9 | `ReadingNotifier.cs:19-21` | Send then save: a crash in between resends | Commit the sent mark right after each send; residual window documented (settled 1) | C5 D3 / `outbox` |
| 10 | `ReadingService.cs:16-23` | Reading and alert in two `SaveChanges`: a failure in between stores the reading and no nurse is told | Alert gets a `Reading` navigation; one `SaveChanges` (one transaction) | C7 D4 / `outbox`, `efcore-patterns` |
| 11 | `ReadingService.cs:7,14` | `CollectedAt` without `Z` (Unspecified) or with `+03:00` (Local): Npgsql refuses it, the reading and its alert are lost with a 500 | `Z` kept; an offset converted to UTC; no offset read as Riyadh time (patients are in Riyadh) | C-W1 S9 D6 / `postgresql-patterns` §2 |
| 12 | `Api/ErrorMiddleware.cs:16` | `ex.Message` and the inner message go to the client (Npgsql text, the meter value or `sub` echoed by `FormatException`) | `AddProblemDetails` + `UseExceptionHandler` with a handler that returns only a title and a `traceId` | C11 S6 D / `api-design` §2 |
| 13 | `ReadingService.cs:25` | `{@Reading}` logs national ID, glucose value and notes | Log the reading id and the alert flag only; add the national-ID masking operator as the net for lines nobody marked | C12 S5 D / `pii-masking` |
| 14 | `Infrastructure/ReadingsDbContext.cs:13-24` | `Notes` has no `HasMaxLength`: an unbounded `text` column (hard ban) | `HasMaxLength(500)` and the same limit in validation | C13 S9 D8 / rules/efcore-rules |

## Warning

| # | Where | What | Fix | Raised by |
|---|---|---|---|---|
| 1 | `ReadingQueries.cs:21` | "Today" is the UTC day: 00:00-03:00 Riyadh shows yesterday | Riyadh day (fixed-offset fallback without tzdata), converted to a UTC range | C-W2 D-W1 / `localization` §7 |
| 2 | `ReadingService.cs:7`, `Domain/Reading.cs:34` | No validation: `Unit` is free text but the threshold assumes mmol/L (every mg/dL reading alerts); a long national ID or note is a 500 | Validator: national ID 10 digits starting 1 or 2, unit `mmol/L` or `mg/dL`, notes ≤ 500, value in range; threshold per unit (11.1 mmol/L = 200 mg/dL) | C-W3 S9 D-W3 |
| 3 | `Program.cs:31,40` | `/health/live` checks the database: a DB blip restarts every pod | Live checks nothing; ready checks the DB (`ready` tag) | C-W4 D-W5 / `api-design` §8 |
| 4 | `Program.cs:20`, `PhotoStore.cs:6` | Settings read with `!`, no start-up validation | Options with `[Required]` + `ValidateOnStart`; the base address gets its trailing `/` | C-W6 / `dotnet-core` |
| 5 | `Api/CurrentPatient.cs:12`, `Program.cs:25-29` | `int.Parse` on the subject: a non-numeric `sub` is a 500 that echoes it; `ClockSkew` 5 min | `MapInboundClaims = false`, read `sub`; the default policy requires a numeric `sub` (403 otherwise); `ClockSkew` 30 s | C-W7 S7 / `auth-patterns` |
| 6 | `Program.cs:30` | No fallback policy: a future endpoint is anonymous | Fallback policy = authenticated user | S8 / `auth-patterns` §3 |
| 7 | `appsettings.json:3` | `Password=change-me` committed | Removed; the password comes from the environment / secret store | S12 D-W6 / `secret-management` |
| 8 | `Program.cs` | No `nosniff`, no `Cache-Control: no-store` on PHI responses, no HSTS | Small header middleware; `UseHsts` outside Development | S13 / rules/security |
| 9 | `HomeReadings.csproj` | Zero-vulns gate not enforced | `NuGetAudit`, `NuGetAuditMode=all`, NU1901-1904 as errors. A scan run here found no vulnerable package | S11 / `zero-vulns` |
| 10 | `ReadingsDbContext.cs:25-32` | No FK or unique index alert → reading | FK + unique index on `ReadingId` | D-W2 |
| 11 | `PhotoStore.cs`, `appsettings.json:12` | Photos on a pod-local path | Needs object storage (infra decision): left, the row now keeps a relative key so a move is a data copy | C-W5 D-W8 |
| 12 | `Api/ReadingEndpoints.cs:11` | `POST /readings` has no idempotency key: an app retry duplicates the reading and the alert | Left: needs a new request field from the app (contract change) | C-W10 D-W4 |
| 13 | `appsettings.json:3` | No pool size / timeouts | Left: to agree with the DBA per replica count | D-W6 |
| 14 | — | No migration scripts | Left: generate the reviewed up/down pair from the model | D-W7 |
| 15 | — | Layering (Application uses Api's `ICurrentPatient` and Infrastructure types) | Left: a restructure, not a production fault | C-W9 |
| 16 | — | No per-caller rate limit; no PHI audit trail | Left: needs product limits and an audit store | S14 S15 |

## Suggestion

- `ReadingsDbContext.cs:31`: partial index on pending alerts (`"SentAt" IS NULL`) instead of `HasIndex(SentAt)` (D). Done.
- `ReadingNotifier.cs:18`: N+1, one query per alert (C-W8, D). Done (one join).
- `ReadingQueries.cs:37`: tie-break by id for a stable "latest" (D). Done.
- `LanguageMiddleware.cs:13`: exact match on `ar` misses `AR`/`ar-SA` (C). Done (`LanguageHeader.Parse`).
- `CareTeamClient.cs:10`: dispose the response (C). Done.
- `Program.cs:7`: scope validation in every environment (K). Done.
- Mixed snake_case tables with PascalCase columns (D); XML docs on public types (C). Left.

## Outcome

All Critical items and Warnings 1-10 are applied in `HomeReadings/`, test first where a test can run
without PostgreSQL (`HomeReadings.Tests/`, 84 tests, green). The database-bound paths (ownership filter,
national-ID pin, single-save alert, notifier lock and per-alert commits) are covered by the build and the
generated DDL only, not by a run against PostgreSQL. Scope validation is on through
`Host.UseDefaultServiceProvider` (same effect as `ValidateServicesInEveryEnvironment`).

## Left, and why

- **National ID from the identity provider or a patient registry** (Critical 4): needs a claim or a service
  this repo doesn't have. The pin to the patient's first reading is the stopgap.
- **The remaining duplicate window** (settled 1): needs an idempotency key, or dedupe on reading + time, from
  the care team.
- **`LO` readings don't alert**: the threshold is the existing high-only rule. Whether a reading below the
  meter's range (a hypo) should page the care team is a clinical decision.
- **Photos to object storage**, **content sniffing of uploads** (only the declared type is checked; nothing is
  served back), **POST idempotency key from the app**, **pool size and timeouts**, **migration scripts**,
  **rate limits**, **PHI audit trail**, **layering**, **naming convention**: see Warnings 11-16.
