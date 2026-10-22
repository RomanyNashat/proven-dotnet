## Security Review: feat/export into main (diff mode)

I picked up after the usage-limit reset and finished under the same instructions. I read the whole diff, every changed file, and the code it calls: `ReadingQueries`, `CurrentPatient`, `ErrorMiddleware`, `ReadingsDbContext`, `Reading`, `ReadingService`, `appsettings.json` and the csproj.

### Critical
1. **A01 IDOR (reaches)** at `HomeReadings/Api/ReadingEndpoints.cs:29-30`, which goes through `HomeReadings/Application/ReadingExport.cs:32-36` to `HomeReadings/Application/ReadingQueries.cs:12-16`.
   - **What's wrong:** the new `GET /readings/{id}/export` calls `ReadingQueries.GetAsync`, which filters only on `r.Id == id` and never on `PatientId`. `ReadingExport` has `ICurrentPatient` injected, but `OneAsync` never uses it. Any logged-in patient can walk the sequential int ids and download any other patient's glucose value, time and free-text notes as CSV.
   - **Fix:** put the owner filter in the query so every caller gets it: `.Where(r => r.Id == id && r.PatientId == patient.Id)` in `GetAsync`. Someone else's record then returns 404 (the endpoint already maps null to NotFound). This also closes the same hole on the existing `GET /readings/{id}`. Add a test: patient B asks for patient A's id and gets 404.
   - **Rule/skill:** `rules/security.md` ("Every lookup by ID checks ownership"), `api-design` (ownership checks on every id), `auth-patterns`:350.

2. **A09 PII in logs (wrote)** at `HomeReadings/Application/ReadingExport.cs:27`.
   - **What's wrong:** every export logs `{NationalId}` in clear at Information level. `appsettings.json:14-17` configures Serilog with Console only: no destructuring policy, no masking operators. The national ID goes straight to the log store, and a national ID on its own identifies the patient.
   - **Fix:** log the internal key and the count only, e.g. `logger.LogInformation("Export for patient {PatientId}: {Rows} rows", patient.Id, rows.Count)`. If the ID has to appear, use a `[LoggerMessage]` with a `[PatientIdentifier]`-classified parameter, plus the `SaudiNationalIdMaskingOperator` pattern layer.
   - **Rule/skill:** `pii-masking` (lines 17-25, 91, 364), `healthcare-compliance`:218 ("No PHI in URLs or logs").

### Warning
3. **A05 CSV/formula injection and row forgery (wrote)** at `HomeReadings/Application/ReadingExport.cs:21-24` and `:36`.
   - **What's wrong:** `Notes` and `Unit` are text the patient typed (`ReadingService.cs:7`, no validator), and they go into the CSV raw. The stated reader is the doctor, who will open the file in Excel.
     - A value starting with `=`, `+`, `-`, `@`, tab or CR runs as a formula (`=HYPERLINK(...)`, DDE).
     - A comma, quote or newline in `Notes` adds columns or fake rows to a clinical document.
   - **Fix:** one `CsvField(string? s)` helper used by both methods:
     - put a `'` in front of any value whose first character is in `=+-@\t\r`;
     - wrap the field in quotes and double any quotes inside it (RFC 4180).
     - Or use CsvHelper with `InjectionOptions.Escape`.
   - **Rule/skill:** `owasp-aspnetcore` (A05; output encoding fitted to the sink).

4. **A04/A02 PHI response can be cached (wrote)** at `HomeReadings/Api/ReadingEndpoints.cs:27,30`.
   - **What's wrong:** both CSV responses carry patient readings with no `Cache-Control: no-store` and no `Content-Disposition`. Nothing in `Program.cs` sets headers globally.
   - **Fix:** set `Cache-Control: no-store` and `Content-Disposition: attachment; filename="readings.csv"` on these endpoints, or set the headers in `OnStarting` as `owasp-aspnetcore`:59 shows.
   - **Rule/skill:** `owasp-aspnetcore`.

### Suggestion
5. **A06 export window has no limit (wrote)** at `HomeReadings/Api/ReadingEndpoints.cs:26` and `HomeReadings/Application/ReadingExport.cs:13-16`.
   - **What's wrong:** `from`/`to` are not validated (`to <= from` is accepted, and so is a range of decades). The whole result is loaded as full tracked-shape entities, including `NationalId` and `PhotoPath`, and built into one string.
   - **Fix:**
     - Reject a range over, say, 366 days, or `to <= from`, with a 400 ValidationProblem.
     - Use `.Select(r => new { r.CollectedAt, r.Value, r.Unit, r.Notes })` so identifiers never leave the database.
     - Put a per-caller rate limit on the export endpoints.
   - **Rule/skill:** `api-design` (validation, per-caller rate limits), `rules/security.md` (input validation).

6. **A10 error detail reaches the client (reaches)** at `HomeReadings/Api/ReadingEndpoints.cs:26`, via `HomeReadings/Api/ErrorMiddleware.cs:16`.
   - **What's wrong:** `from`/`to` are written to `timestamptz` columns (`ReadingsDbContext.cs:20`). If a client sends a date with no UTC designator and it binds with `Kind=Unspecified`, Npgsql throws. The new endpoint then sends the provider's exception message to the client through the existing `ex.Message`/`InnerException` echo. I haven't proven that binding path here.
   - **Fix:** take `DateTimeOffset` (or call `DateTime.SpecifyKind(..., Utc)` after validating), and make `ErrorMiddleware` return ProblemDetails with only a `traceId`.
   - **Rule/skill:** `api-design`:104, `rules/architecture.md` (error handling).

7. **A09 no audit record of PHI export (wrote)** at `HomeReadings/Application/ReadingExport.cs:27-36`.
   - **What's wrong:** a bulk export and a share of PHI are data-access events. The only trace is an Information log line, and `OneAsync` writes nothing at all.
   - **Fix:** write an audit entry with patient id, reading ids or range, row count and time for both methods.
   - **Rule/skill:** `healthcare-compliance` (audit trail of data access).

### Known-traps row
`Program.cs:7`, ValidateScopes/ValidateOnBuild: **dismissed for this change.** The change adds `AddScoped<ReadingExport>()` at `Program.cs:18`. Its dependencies are the DbContext, `ICurrentPatient` and `ReadingQueries` (all scoped) and an `ILogger` (singleton), so it adds no captive dependency or lifetime risk. Line 7 itself isn't edited. This is not a security issue either way; it belongs to `dotnet-core`, for the code-reviewer.

### Not checked
- I didn't run `dotnet list package --vulnerable --include-transitive`, because I was confined to reading. The diff doesn't touch the csproj, so the change adds no new dependency.
- No build or test run.
- `.gitignore` (`bin/`, `obj/`) has no security effect.

Outside this change: 11