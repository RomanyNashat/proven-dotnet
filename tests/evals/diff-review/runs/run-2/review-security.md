## Security Review: feat/export into main (diff, security-reviewer)

I read the whole of each changed file, plus the code they call: ReadingQueries, ICurrentPatient, Reading, ReadingsDbContext, ErrorMiddleware, LanguageMiddleware, Program.cs and appsettings.json. Skills cited are under /home/claude/proven-dotnet/.claude/skills/.

### Critical
1. **A01 IDOR (wrote + reaches)**: `HomeReadings/Api/ReadingEndpoints.cs:29-30` and `HomeReadings/Application/ReadingExport.cs:34`
   - **What's wrong:** the new `GET /readings/{id:int}/export` calls `ReadingQueries.GetAsync(id)` (`ReadingQueries.cs:12-16`). That query filters on `r.Id == id` only and never checks `PatientId`. Ids are sequential ints, so any logged-in patient can loop over ids and download every other patient's glucose readings and free-text notes as CSV. `ReadingExport` already has `patient` injected, but `OneAsync` doesn't use it.
   - **Fix:** filter by owner in the query, `.Where(r => r.Id == id && r.PatientId == patient.Id)`. Fixing it inside `GetAsync` also closes the older `GET /readings/{id}`. Someone else's reading returns 404, as the code already does for null.
   - **Rule/skill:** `rules/security.md` (every lookup by ID checks ownership), `auth-patterns` §4, `owasp-aspnetcore` A01.

2. **A09 PII in logs (wrote)**: `HomeReadings/Application/ReadingExport.cs:27`
   - **What's wrong:** every export logs the patient's national ID in clear as a scalar (`{NationalId}`). Nothing masks it: `Program.cs:9` only reads config, and `appsettings.json:14-17` has no masking enricher or destructuring policy. It goes straight to Elastic.
   - **Fix:** log `patient.Id` (the internal int) and the row count, never the national ID. If a national ID ever has to be logged, use a `[LoggerMessage]` parameter marked `[PatientIdentifier]` and add `AddPiiMasking()` to the Serilog config.
   - **Related:** the query loads the full entity (NationalId, PhotoPath) only to build this log line. Project just the four exported columns (minimum necessary).
   - **Skill:** `pii-masking`.

### Warning
3. **A05 CSV/formula injection and row forgery (wrote)**: `ReadingExport.cs:21-24` and `:36`
   - **What's wrong:** `Notes` (unbounded, unvalidated free text from the patient) and `Unit` go into the CSV without quoting or escaping.
     - A comma or newline in a note splits the row or adds fake rows to a clinical export a doctor relies on.
     - A note starting with `=`, `+`, `-`, `@`, tab or CR runs as a formula when the doctor opens the file in Excel (e.g. `=HYPERLINK(...)` to send data out).
   - **Fix:** use one CSV writer for both methods. Quote every text field per RFC 4180 (`"` + value with `"` doubled + `"`), and prefix `'` when a field starts with `= + - @ \t \r`.
   - **Rule/skill:** `rules/security.md` (input validation), `owasp-aspnetcore` A05.

4. **A06/A10 unvalidated date range reaches an error leak (wrote + reaches)**: `ReadingEndpoints.cs:26`, `ReadingExport.cs:13-16`
   - **What's wrong, part 1:** `from` and `to` have no validation. Nothing checks `from < to` and nothing limits the span, so `from=0001-01-01&to=9999-12-31` loads a patient's whole history into memory with `ToListAsync` and builds it into one string. Nothing rate-limits the endpoint.
   - **What's wrong, part 2:** a value with no offset binds as `Kind=Unspecified`, and Npgsql throws when it compares that with `timestamptz` (`postgresql-patterns` §2). The exception reaches `ErrorMiddleware.cs:16`, which writes `ex.Message` and `InnerException.Message` to the client. So the change adds an easy way for any caller to get internal error text back.
   - **Fix:**
     - Take `DateTimeOffset`, or call `DateTime.SpecifyKind`/`ToUniversalTime` before querying.
     - Validate `from < to` and a maximum span (e.g. 366 days), and return a ValidationProblem when it fails.
     - Add `.RequireRateLimiting("per-patient")` with a partition per caller (`api-design` §6).
     - The general `ErrorMiddleware` leak is older code. Replace it with ProblemDetails plus a trace id.

### Suggestion
5. **A09 audit (wrote)**: `ReadingExport.cs:11` and `:32`. An export sends PHI outside the system, to a doctor, and nothing records it. Add an append-only audit row for each export: who, which patient, the range or id, and when (`healthcare-compliance`, the PHI audit; "Reads aren't changes" covers access).
6. **Data integrity (wrote)**: `ReadingExport.cs:22` and `:36`. `Append(r.Value)` and `{reading.Value}` format with the current culture, and `LanguageMiddleware.cs:14` sets that to `ar-SA` when `x-language: ar`. A clinical value can then go out with Arabic digits or separators. Use `CultureInfo.InvariantCulture` (`rules/csharp-standards.md`, `localization`). This is mainly the code-reviewer's area.

### Known-traps item: `Program.cs:7`, scope validation (info): dismissed for this change
The change only adds `AddScoped<ReadingExport>()` (`Program.cs:18`). Its dependencies (DbContext, ICurrentPatient, ReadingQueries, ILogger) are scoped or safe for scoped use, and only request-scoped endpoint handlers resolve it. No singleton captures it: `ReadingNotifier` creates its own scope and never resolves it. The missing `ValidateScopes`/`ValidateOnBuild` is older code, and this change neither writes nor reaches a flaw through it. It is still worth fixing on its own (`dotnet-core`).

`.gitignore` (bin/obj): nothing to report.

Outside this change: 9

Not checked: `dotnet list package --vulnerable` (no build or package run in this review), `PhotoStore.cs`, `ReadingNotifier.cs` and `CareTeamClient.cs` beyond their DI wiring, and the tests (there are none in the repo).
