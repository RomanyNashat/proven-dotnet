## Database Review: feat/export (CSV export of readings), diff vs main

Picked up where I stopped after the limit reset, under the same instructions. I read the whole of every changed file, plus ReadingQueries.cs, ReadingsDbContext.cs, Reading.cs, CurrentPatient.cs, LanguageMiddleware.cs and ReadingService.cs.

### Critical
1. **Ownership / IDOR** `HomeReadings/Api/ReadingEndpoints.cs:29-30` → `Application/ReadingExport.cs:34` → `Application/ReadingQueries.cs:12-16`. **Reaches.** The new `GET /readings/{id}/export` calls `ReadingQueries.GetAsync(id)`, which filters on `r.Id == id` only, with no `PatientId == patient.Id`. Ids are sequential `int`s, so any signed-in patient can walk `/readings/1/export`, `/2/export` and so on, and download other patients' glucose values and notes as CSV. The flaw was already in `GetAsync`; this change adds a new endpoint over it, built for sharing.
   **Impact:** health data leaks across patients. Costs one PK lookup per guess, so scraping everything is cheap.
   **Fix:** filter by owner in the query: `.Where(r => r.Id == id && r.PatientId == patient.Id)`, either in `GetAsync` (this also fixes the existing `GET /{id}`) or in a separate owner-scoped query that `OneAsync` uses. Return 404 for someone else's id. Add a test where patient B asks for A's reading and gets 404. Rule: `rules/security.md` (every lookup by ID checks ownership); `efcore-patterns` §query filters (a filter is not the ownership check).

### Warnings
1. **DateTime Kind / Npgsql** `ReadingEndpoints.cs:26`, `ReadingExport.cs:14`. **Wrote.** `from` and `to` are bound from the query string. Values like `?from=2026-10-01&to=2026-10-22` have no offset, so they bind as `Kind=Unspecified`. Npgsql refuses a non-UTC `DateTime` compared against a `timestamptz` column (`CollectedAt`), so the most natural call fails with a 500. **Fix:** take `DateTimeOffset` and pass `.UtcDateTime`, or reject non-UTC input. Skill: `postgresql-patterns` §2 ("Any DateTime goes in" row).
2. **Unbounded read** `ReadingExport.cs:13-16, 18-25`. **Wrote.** No cap on the range, no check that `from < to`, no row limit. The whole range is loaded as tracked-free entities into a list, and then copied again into one `StringBuilder` string. A multi-year range for a patient whose meter sends readings often could be many thousands of rows, held in memory twice per request.
   **Fix:** validate the range and cap it (for example 90 days). Project only the four exported columns (`Select(r => new { r.CollectedAt, r.Value, r.Unit, r.Notes })`) instead of whole entities, which drags `NationalId` and `PhotoPath` along. For big ranges, stream with `AsAsyncEnumerable()` into the response. Skill: `efcore-patterns` (projections); `rules/performance.md`.
3. **PII in logs** `ReadingExport.cs:27`. **Wrote.** It logs the patient's `NationalId`, a government ID, to Elastic on every export, and it is the only reason that column is fetched. **Fix:** log the patient's int id and the row count only. Rule: `rules/security.md` (Data Protection). Overlaps the security reviewer.
4. **Export formatting culture** `ReadingExport.cs:22` and `:36`. **Wrote.** `LanguageMiddleware` sets `CurrentCulture` to `ar-SA` when `x-language: ar`. `.Append(r.Value)` and the interpolated `{reading.Value}` then format with the Arabic decimal separator (`٫`), so the doctor's CSV changes depending on the app language. On an image without ICU, creating `ar-SA` throws. (`:O` on `CollectedAt` is invariant, so it is fine.) **Fix:** format with `CultureInfo.InvariantCulture`. Rule: `rules/csharp-standards.md`; skill `localization` §1.

### Suggestions
1. `ReadingExport.cs:21-24, 36`. **Wrote.** `Notes` and `Unit` are written raw: a comma or newline in `Notes` breaks the row, and a leading `=`/`+`/`-`/`@` is a CSV formula injection. Quote and escape the fields, or use a CSV writer. (Mostly the code and security reviewers' area.)
2. `ReadingExport.cs:15`. `OrderBy(CollectedAt)` alone gives an undefined order for readings at the same instant. Add `.ThenBy(r => r.Id)` so exports come out the same every time. It is not paging, so no rows are lost.

### Index Recommendations
- None needed. `AllAsync` (`patient_id = @p AND collected_at >= @f AND collected_at < @t ORDER BY collected_at`) is a range scan on the existing `IX_readings_PatientId_CollectedAt` (`ReadingsDbContext.cs:23`). The fixed `OneAsync` uses the primary key. Confirm with `EXPLAIN (ANALYZE, BUFFERS)` on a realistic range.

### Migration Safety
- No schema or migration change. Safe for zero-downtime; nothing to check for N-1 compatibility.

### Known-traps row (Program.cs:7, scope validation): dismissed for this change
The change only adds `AddScoped<ReadingExport>()` (`Program.cs:18`). Its dependencies (DbContext, `ICurrentPatient`, `ReadingQueries`) are all scoped, and only request handlers resolve it, so the change adds no captive dependency. The missing `ValidateScopes`/`ValidateOnBuild` was there before and is still worth fixing (`dotnet-core`), but this change neither wrote it nor reaches it.

Outside this change: 8

Not checked: I couldn't run anything (no build, no EXPLAIN). I took Npgsql's DateTime binding and rejection behaviour from `postgresql-patterns` §2, not from running it here. Connection-string pool sizing: I didn't open appsettings secrets.
