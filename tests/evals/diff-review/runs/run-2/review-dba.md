## Database Review: feat/export (diff vs main), dba-reviewer

I picked up where I stopped when the usage limit hit and finished under the same instructions. I read every changed file in full, plus the code it calls: ReadingQueries, ReadingsDbContext, Reading, CurrentPatient, LanguageMiddleware, ReadingService and appsettings.

### Critical
1. **[Ownership / IDOR]** `HomeReadings/Api/ReadingEndpoints.cs:29-30` → `Application/ReadingExport.cs:34` → `Application/ReadingQueries.cs:12-16`. **Reaches.** `OneAsync` calls `queries.GetAsync(id)`, and that query filters only on `r.Id == id`, with no `PatientId == patient.Id`. The new `/readings/{id}/export` endpoint lets any signed-in patient walk the sequential int ids and download other patients' glucose readings and notes as CSV. `ReadingExport` takes `ICurrentPatient` as a dependency but `OneAsync` never uses it.
   **Fix:** add `&& r.PatientId == patient.Id` to `GetAsync`. That also closes the same hole on the existing `GET /readings/{id}`. Or use an owner query filter, as long as you don't rely on the filter alone. Add a test where patient B's id returns 404 for patient A. (`rules/security.md` "Every lookup by ID checks ownership"; `efcore-patterns` owner filter, lines 137-172)
2. **[PHI in logs]** `Application/ReadingExport.cs:27`. **Wrote.** The line logs `NationalId` in plain text at Information level to the console sink. It is also the only reason the query loads the whole entity. **Fix:** remove it. Log the row count and the internal `patient.Id` only, or mark the value as a patient identifier for redaction. (`pii-masking`; `rules/security.md` Data Protection)

### Warnings
3. **[Unbounded read / full entity loaded]** `Application/ReadingExport.cs:13-16`. **Wrote.** The query loads whole `Reading` entities (NationalId, PhotoPath, ReceivedAt) to use 4 columns, and `from`/`to` have no limit and no check that `from < to`. A request like `from=0001-01-01&to=9999-12-31` pulls a patient's whole history into memory and builds it into one string. Rough size: a CGM sending every 5 minutes gives about 105k rows a year, so tens of MB per request. Multi-year histories, or several of these calls at once, put real pressure on pod memory. The `(PatientId, CollectedAt)` index covers the WHERE and ORDER BY, so the cost is the number of rows, not the plan.
   **Fix:** `.Select(r => new { r.CollectedAt, r.Value, r.Unit, r.Notes })`, reject `to <= from` and ranges over N days (for example 366) with a 400, and stream the CSV through `AsAsyncEnumerable()` to the response instead of building one `StringBuilder`. (`efcore-patterns` line 342 "a projection for every read")
4. **[timestamptz / DateTime Kind]** `Api/ReadingEndpoints.cs:26` and `Application/ReadingExport.cs:14`. **Wrote.** Raw `DateTime from/to` from the query string are compared with a `timestamptz` column. Through EF, Npgsql throws when `Kind != Utc`, which gives a 500. Whether `?from=2026-10-01` binds as Utc or Unspecified depends on the binder's parse styles. I did not run it, so a test needs to confirm it. Even when it binds, the day edges are UTC midnight while the patient means Riyadh days (3h shift: readings between 00:00 and 03:00 local land on the wrong day).
   **Fix:** take `DateOnly from, DateOnly to`, convert them through the patient's time zone to UTC instants (`DateTimeKind.Utc`), and test it with a date-only value. (`postgresql-patterns` §2, line 55)
5. **[Culture in machine output]** `Application/ReadingExport.cs:22` (`Append(decimal)`) and `:36` (interpolated `{reading.Value}`). **Wrote.** `LanguageMiddleware` sets `CurrentCulture = ar-SA` when `x-language: ar` is sent, so values come out as `7٫5`, and the CSV the doctor gets cannot be parsed. **Fix:** format with `CultureInfo.InvariantCulture` (`string.Create(CultureInfo.InvariantCulture, ...)`). `:O` on the date is already invariant. (`localization` line 24; `rules/csharp-standards.md` InvariantCulture / ar-SA)
6. **[CSV escaping]** `Application/ReadingExport.cs:21-24, 36`. **Wrote.** `Notes` and `Unit` are written unquoted. A comma, quote or newline in a note breaks the rows, and a note starting with `=`, `+`, `-` or `@` runs as a formula when the doctor opens the file in Excel. **Fix:** quote every field per RFC 4180 (double any `"`), and put `'` in front of a leading `= + - @` (or use CsvHelper). (`owasp-aspnetcore`)

### Suggestions
7. `Api/ReadingEndpoints.cs:26`: add `Content-Disposition: attachment; filename=...` (`TypedResults.File` or a header) and `charset=utf-8`, so Arabic notes open correctly.

### Known-traps row (`Program.cs:7`, ValidateScopes): dismissed for this change
The change only adds `Program.cs:18`, `AddScoped<ReadingExport>()`. Its dependencies are a scoped DbContext, scoped ICurrentPatient, scoped ReadingQueries and a singleton ILogger, and only endpoints consume it, so it adds no captive dependency. The setting being off in Production is an existing problem and is in the count below.

### Index Recommendations
- None needed. The existing `IX_readings_PatientId_CollectedAt` on `(patient_id, collected_at)` covers the export's WHERE and ORDER BY (`Infrastructure/ReadingsDbContext.cs:23`), and `OneAsync` looks up by primary key.

### Migration Safety
- The change has no migration and no schema change, so it is N-1 safe.

Not checked: whether the binder's DateTime Kind matches what I expect (finding 4 needs a test), anything at runtime or in EXPLAIN, ReadingNotifier/CareTeamClient (not reached by the change), and the other two reviewers' areas beyond where they overlap with 2 and 6.

Outside this change: 8