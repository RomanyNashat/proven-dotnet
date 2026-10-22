## Database Review: feat/export (reading CSV export), diff only

### Critical
1. **Ownership / IDOR (reaches)** `HomeReadings/Application/ReadingExport.cs:59` calls `ReadingQueries.GetAsync` (`ReadingQueries.cs:12-16`). That query filters on `r.Id == id` only, with no `PatientId` check. The new endpoint `GET /readings/{id:int}/export` (`ReadingEndpoints.cs:29-30`) is a second way in to that unsafe query. Any authenticated patient can walk the sequential int ids and download any other patient's glucose value, unit, time and free-text notes as CSV.
   **Fix:** filter on the owner inside the query, `.Where(r => r.Id == id && r.PatientId == patient.Id)` in `GetAsync` (this also closes the existing `/{id}` endpoint), or give `OneAsync` its own owner-filtered query. It costs nothing extra: the PK seek already returns the row and `PatientId` is checked on it. Not-owned returns 404. Rule: `rules/security.md` ("Every lookup by ID checks ownership"), `rules/proven/data.md`.

### Warnings
2. **DateTime Kind into timestamptz (wrote)** `ReadingEndpoints.cs:26` binds `DateTime from, DateTime to` from the query string, and `ReadingExport.cs:39` compares them to the `timestamptz` column `CollectedAt`. A plain date like `?from=2026-10-01` binds with `Kind=Unspecified`, and Npgsql throws when it writes that parameter, so the endpoint returns a 500. Only values ending in `Z` or carrying an offset work. Even then the range is a UTC day, not the patient's day: a Riyadh patient's readings between 21:00 and 24:00 local land on the wrong side of the edge.
   **Fix:** take `DateOnly from, DateOnly to` (or `DateTimeOffset`). Convert to UTC bounds in the patient's zone (Asia/Riyadh, with the fixed-offset fallback when tzdata is missing). Reject `from >= to`. Skills: `postgresql-patterns` §2 (DateTime table, line 55), `localization` §7.
3. **Culture-dependent numbers in a machine file (wrote)** `ReadingExport.cs:47` (`StringBuilder.Append(decimal)`) and `:61` (interpolated `{reading.Value}`) format with the current culture. `LanguageMiddleware.cs:14` sets that culture to `ar-SA` when `x-language: ar` is sent. Arabic-language patients then get values like `7٫5` in the CSV, which the doctor's tools can't parse. `:O` on the date is invariant and fine.
   **Fix:** `Append(r.Value.ToString(CultureInfo.InvariantCulture))`, and `string.Create(CultureInfo.InvariantCulture, $"...")` for line 61. Skill: `localization` (number table line 24; review list line 438).
4. **PHI in logs, and the full entity loaded to get it (wrote)** `ReadingExport.cs:52` logs `NationalId`. That is the only reason `AllAsync` loads whole `Reading` entities (including `NationalId` and `PhotoPath`) instead of the four exported columns. Also flagged for security-reviewer.
   **Fix:** drop `NationalId` from the log (log the count, plus the patient id if needed). Project instead: `.Select(r => new { r.CollectedAt, r.Value, r.Unit, r.Notes })`. Rules: `rules/security.md` Data Protection, `postgresql-patterns` (Include Error Detail / PHI note).
5. **CSV not escaped (wrote)** `ReadingExport.cs:49` and `:61` write `Notes` (free text) and `Unit` without quoting. A comma, quote or newline in a note shifts columns or splits rows in the file sent to the doctor. A leading `=`, `+`, `-` or `@` is a spreadsheet formula injection risk.
   **Fix:** RFC 4180 quoting (wrap in quotes, double any embedded quotes), and prefix formula-leading cells with `'`. Code-reviewer and security-reviewer may also report this.

### Suggestions
6. **Unbounded range, whole result in memory (wrote)** `ReadingExport.cs:36-53`. `from=0001-01-01&to=9999-12-31` reads every reading the patient has into a list and then a string. One patient at about 4 readings a day is about 1,500 rows a year, so this is small today, but nothing caps it. **Fix:** cap the range (for example 1 year) in validation. Optionally stream with `AsAsyncEnumerable()` into the response.

### Index Recommendations
- None needed. `AllAsync` is a range seek on the existing `IX_readings_PatientId_CollectedAt` (`ReadingsDbContext.cs:23`), and its `OrderBy(CollectedAt)` follows the index order. `OneAsync` is a PK seek. An `INCLUDE (value, unit, notes)` for index-only scans isn't worth it at per-patient volumes.

### Migration Safety
- No schema change and no migration in this diff. Safe for N-1 deployment.

### Known-traps row (`Program.cs:7`, scope validation): dismissed for this change
The change only adds `AddScoped<ReadingExport>()` (`Program.cs:18`). Its only consumers are request-scoped endpoint handlers. The one singleton, `ReadingNotifier`, uses `IServiceScopeFactory` and doesn't take `ReadingExport`, so the change creates no captive dependency and doesn't reach the gap. The missing `ValidateScopes`/`ValidateOnBuild` setting itself is older config (`dotnet-core` line 326) and is counted below.

Outside this change: 5

Not checked: Dapper (none in the change), runtime EXPLAIN plans (no database available), and the HTTP/auth pipeline beyond the ownership point (security-reviewer's area). Roslyn MCP wasn't available in this run, so I traced callers by reading the files; the call graph here is only a few files.