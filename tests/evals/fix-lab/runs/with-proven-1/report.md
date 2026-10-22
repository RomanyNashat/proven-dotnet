## HomeReadings: review and fixes done

`dotnet build HomeReadings/HomeReadings.csproj` succeeds. The tests are in the sibling folder `HomeReadings.Tests/`: 84 of them, all passing. They run without PostgreSQL: unit tests, plus `WebApplicationFactory` tests in Production against a database that refuses connections. 13 of those tests failed against the original code before I changed anything (500s where 400/403 were expected, raw error JSON, 4 POSTs instead of 1). Nothing has run against a real PostgreSQL. Line coverage is 67%, branch 68%, below the 80/70 bar; what's uncovered is the database-bound code. The vulnerable-package scan is clean.

**Consolidated review:** `/tmp/claude-0/labruns/with-proven-1/review.md` (Critical, Warning, Suggestion, plus Outcome and Left). Disagreements I settled:
- **Alerts:** at-least-once, not parking unclear sends as "Unknown". A missed alert on a high reading is worse than a duplicate, and a duplicate carries the same ID, value and time.
- **Several pods sending:** one sender behind a PostgreSQL session advisory lock, not row claims.
- **The "latest" cache:** removed rather than re-keyed per patient.
- **Audience:** kept as the constant `home-readings`, because `task.md` fixes it.

### What I fixed (file: what, why)
- `Application/ReadingQueries.cs`: `GetAsync` now filters by the caller's patient id (IDOR). The global `readings:latest` cache is gone, so patients no longer see each other's readings. "Today" is now the Riyadh day. Ties are broken by id.
- `Infrastructure/PhotoStore.cs`: the server makes the file name (`{readingId}/{guid v7}.{ext}`) and checks the path stays under the root (path traversal). Only JPEG/PNG/HEIC are accepted, up to 10 MB. The reading stores a relative key. Options are validated at start-up.
- `Api/ReadingEndpoints.cs`: FluentValidation runs first and bad input gets a 400 ValidationProblem. Photos are checked before saving, with a request size limit. Typed results. The response shapes are unchanged.
- `Domain/MeterValue.cs`, `GlucoseUnit.cs`, `NationalId.cs`, `Digits.cs`: meter text is parsed with the invariant culture. Arabic-Indic digits and `٫` are accepted. `,` is refused because it's ambiguous (it gave 125 under en-US). `HI`/`LO` are mapped and `HI` alerts. Units are limited to mmol/L or mg/dL.
- `Domain/Reading.cs`: the alert threshold now depends on the unit (mg/dL readings all used to alert). New `OutOfRange` column. `Record(...)` creates the alert with its reading, so both go in one `SaveChanges`; a high reading can no longer be saved without its alert.
- `Domain/CareTeamAlert.cs`: records `Attempts`, `LastError` (exception type and status only) and `LastAttemptAt`. The existing constructor and members are kept.
- `Application/ReadingNotifier.cs`: one sender across pods (advisory lock). Readings come from one join instead of a query per alert. Each alert is marked sent right after its POST. A failure is recorded on the alert and `RunOnceAsync` doesn't throw: a 4xx moves on to the next alert, an outage stops the batch. `ExecuteAsync` catches each run, so a care-team outage no longer stops the host.
- `Program.cs`:
  - the care team POST is never retried (`DisableForUnsafeHttpMethods`)
  - scope and build validation on in every environment
  - options validated at start-up, with a trailing `/` added to the base address
  - `MapInboundClaims=false` and `ClockSkew` 30 s
  - default and fallback policy require a numeric `sub` (403 otherwise)
  - ProblemDetails with `UseExceptionHandler`
  - liveness checks nothing; readiness checks the database
  - HSTS and security headers
  - PII masking added to Serilog, still read from the `Serilog` section
- `Application/ReadingService.cs`: the log line has the reading id only (it used to log the national ID, value and notes). The national ID must match the patient's earlier readings. The photo lookup checks the owner, and the file is deleted if the database save fails. `CollectedAt` is converted to UTC; a time with no offset is read as Riyadh time.
- `Api/UnexpectedExceptionHandler.cs`: replaces `ErrorMiddleware`, which sent `ex.Message` to the client. Now the client gets a ProblemDetails with a `traceId` and nothing else.
- `Application/LanguageHeader.cs`, `Messages.cs`: `x-language` picks the text only, not the formatting culture. Lookup is by language code, so no ICU is needed. `LanguageMiddleware` is deleted.
- `Infrastructure/ReadingsDbContext.cs`: `Notes` is now `varchar(500)` (was unbounded `text`). Added FK alert → reading (restrict delete) with a unique index, a partial pending-alerts index, and a `Value > 0` check.
- `Infrastructure/CareTeamClient.cs`: the response is disposed; the options class is added. `Infrastructure/LoggingSetup.cs`: masks Saudi national IDs in logs.
- `appsettings.json`: removed the committed `Password=change-me`. `HomeReadings.csproj`: NuGetAudit with NU1901-1904 as errors; added FluentValidation 12.2.0 and Serilog.Enrichers.Sensitive 2.1.0.

### Risks to the hidden tests
- A later reading whose `nationalId` differs from the patient's first one now gets a 400.
- National IDs must be 10 digits starting with 1 or 2.
- A photo needs an image content type.
- Alerts now have an FK, so an alert inserted for a reading that doesn't exist will fail.
- Resolving `ReadingsDbContext` from the root provider now throws, because scope validation is on.

### Left, and why
- **National ID from the identity provider or a patient registry:** neither exists in this repo. Pinning it to the patient's first reading is a stopgap.
- **The last duplicate window** (a crash or timeout after a POST that arrived): needs an idempotency key from the care team.
- **Whether `LO` readings should alert:** a clinical decision.
- Also left: object storage for photos, checking upload bytes (only the declared type is checked), an idempotency key on `POST /readings` (needs an app change), pool sizing (DBA), migration scripts, rate limits, a PHI audit trail, layering, naming.

**Not checked:** anything against a real PostgreSQL (the advisory lock SQL, the join, the single-save alert, the ownership filters), the Keycloak `sub` format, and nginx/ingress body limits.

**Worth remembering:** a `cat >` with no input blocked a Bash call until it timed out. Pass `< /dev/null` to dotnet commands in chained shell calls.