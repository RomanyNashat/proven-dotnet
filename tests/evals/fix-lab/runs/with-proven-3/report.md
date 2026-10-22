I merged the scan and the three reviews into one review and applied it to the service. `dotnet build HomeReadings/HomeReadings.csproj` succeeds, and the new test project passes 82 of 82. Nothing ran against PostgreSQL, so the SQL parts are checked by reading only (listed at the end).

**Files**
- Review (merged, ranked, disagreements settled, status per item): /tmp/claude-0/labruns/with-proven-3/review.md
- Tests: /tmp/claude-0/labruns/with-proven-3/HomeReadings.Tests/ (xUnit, FluentAssertions `[7.0.0,8.0.0)`, WebApplicationFactory in Production). The project runs without ICU, like the slim image.
- SQL scripts: /tmp/claude-0/labruns/with-proven-3/HomeReadings/db/001_create_readings_schema.{up,down}.sql

**TDD:** 21 tests were written against the original code and all failed for the intended reason, for example 4 POSTs to the care team instead of 1, the notifier's loop faulting, and a 500 on every request without ICU. The tests for the new types started from stubs and failed first. The schema test and the Development host test were written after the fix, as guards.

**Constraints kept:** routes and request/response shapes, `ReadingsDbContext`, `ReadingNotifier.RunOnceAsync`, `CareTeamClient` as a typed client, `TimeProvider` from DI, JWT on the default scheme (`Auth:Authority`, audience `home-readings`, `sub` read as the patient id), the setting names, and Serilog from the `Serilog` section. Additive only: an `outOfRange` field on readings, and `value` is null for HI/LO.

**Fixed (file: what, why)**
- `Application/ReadingNotifier.cs`: each run is caught and logged, so a failed run no longer stops the host or crash-loops the pod.
  - One runner across pods (a transaction-scoped advisory lock on its own connection).
  - Each alert is marked as being sent, and committed, before its POST. The result is saved even during shutdown, and alerts are loaded in one query (no N+1).
  - An alert is resent only when it certainly didn't arrive. If delivery is unknown, or the care team refused it, it gets `NeedsFollowUpAt` and an error log instead. This is because the care team has no idempotency key.
- `Infrastructure/CareTeamRegistration.cs`: POST is never retried (`DisableForUnsafeHttpMethods`), and the base address is validated at startup.
- `Infrastructure/CareTeamClient.cs`: sorts each outcome into Delivered, Failed, Rejected or Unknown. Values are sent in mmol/L, with `unit`.
- `Domain/MeterValue.cs`, `Domain/GlucoseUnit.cs`, `Domain/Reading.cs`: the meter's text is parsed without the request culture. HI/LO, `5,6` and Arabic-Indic digits are accepted, and HI always alerts. The alert threshold now respects mg/dL vs mmol/L.
- `Application/NewReadingValidator.cs`: validation returns a 400 ValidationProblem.
  - National ID must be 10 digits.
  - Unit must be on the allowlist.
  - Notes are capped at 500, the same as the column.
  - `collectedAt` is required. With no offset it is read as Riyadh time; otherwise it is converted to UTC (Npgsql refused the old `DateTime`).
- `Application/ReadingService.cs`: the reading and its alert are saved in one `SaveChanges`. The national ID must match the patient's earlier readings. The log carries no health data. The photo lookup is by owner.
- `Application/ReadingQueries.cs`:
  - The IDOR is fixed: every query filters by the patient.
  - The shared "latest" cache is removed.
  - "Today" is the Riyadh day.
  - Results have a stable sort order.
- `Api/LanguageMiddleware.cs` was deleted and replaced by `Application/LanguageHeader.cs` and `Messages.Saved(language)`. Creating ar-SA or en-US throws without ICU, so before this every request was a 500.
- `Api/ErrorMiddleware.cs` was deleted and replaced by `Api/UnexpectedExceptionHandler.cs`: errors are ProblemDetails with a `traceId` and no exception text.
- `Infrastructure/PhotoStore.cs`: the client's file name is never used; the server names the file and the row keeps the relative key. Only JPEG, PNG, HEIC or WebP (checked by content) up to 10 MB are accepted, with matching endpoint limits. This closes the path traversal.
- `Infrastructure/ReadingsDbContext.cs`: `Notes` is now `varchar(500)` instead of `text`. Added the alert→reading foreign key, a partial index on waiting alerts, and the new alert columns.
- `Program.cs`, `Api/PatientClaims.cs`:
  - A `sub` that isn't a positive integer gets 403 instead of a 500.
  - Every endpoint needs a signed-in user unless it opts out.
  - ClockSkew is 30 s and `MapInboundClaims` is false.
  - Liveness no longer checks the database; readiness does.
  - Options are validated at startup.
  - Every response gets `no-store`, `nosniff` and `X-Frame-Options`, and HSTS is on outside Development.
- `appsettings.json`: the committed password is removed, and the pool is sized to 20 with timeouts. `HomeReadings.csproj` gets a `UserSecretsId` for the local password.

**Left, and why**
- **Scope validation in Production:** not turned on. Nothing is captured today, and in Production it makes any root-provider resolution of a scoped service throw. A test builds the host in Development, where it is on.
- **National ID source:** only partly fixed (the check against earlier readings). The real fix is a claim from the identity provider or a patient registry.
- **Care team questions:** whether they can take an idempotency key, whether a 5xx can mean accepted (5xx is retried today), and agreeing the new `unit`/`outOfRange` fields and `value: null` for HI.
- **Clinical:** whether LO or low numbers should alert. Only high readings alert, as before.
- **Owners needed:** who acts on `NeedsFollowUpAt`; rate limits and their numbers; encrypting the national ID at rest; object storage for photos and ingress body limits; ForwardedHeaders.
- **Not done:** the zero-vulns props file; snake_case column names; `EnableRetryOnFailure` (it could duplicate inserts).

**Not checked here (no PostgreSQL):** the advisory-lock SQL and its concurrency, the EF translation of the queries, two runners at once, the national-ID check, and the photo endpoint end to end.

**Side effect outside the run folder:** the first RED run of the photo test let the old code write two 12-byte files outside the run folder, at `/tmp/escaped-absolute.jpg` and `/tmp/home-readings-photo-tests/escaped.jpg`. I deleted them and the temp folders, and the tests now write only under their output folder. I didn't run git.