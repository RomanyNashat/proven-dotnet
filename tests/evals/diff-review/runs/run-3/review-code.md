## Code Review: feat/export into main (HomeReadings CSV export)

### Summary
The range export (`AllAsync`) correctly filters by the signed-in patient. The single-reading export does not: it opens another patient's health data to anyone who guesses an id. The change also logs the national ID, and it writes the CSV in the request culture with no escaping. Do not merge until the two Critical items are fixed.

### Critical (must fix)
1. **IDOR / ownership (reaches)** `HomeReadings/Api/ReadingEndpoints.cs:29-30` → `HomeReadings/Application/ReadingExport.cs:34`. The new `GET /readings/{id:int}/export` calls the existing `ReadingQueries.GetAsync` (`ReadingQueries.cs:12-16`). That query filters only on `r.Id == id`, never on `PatientId`. Ids are sequential ints, so any authenticated patient can loop over ids and download other patients' glucose values and notes as a file meant for sharing.
   **Fix**: filter by the caller in the query, `.Where(r => r.Id == id && r.PatientId == patient.Id)`, in `GetAsync` itself (better) or in a dedicated export query. Another patient's id must return 404. Rule: `rules/security.md` "Every lookup by ID checks ownership"; `api-design` §11, first bullet.
2. **PHI in logs (wrote)** `HomeReadings/Application/ReadingExport.cs:27`. `LogInformation("Export for {NationalId}...", rows.FirstOrDefault()?.NationalId, ...)` writes the patient's national ID to Elastic as plain text. It is also wrong data: it logs null when there are no rows.
   **Fix**: log the internal `patient.Id` and the row count, or nothing. If an identifier is ever needed, mark it `[PatientIdentifier]` with redaction. Skill: `pii-masking` (classified logging); `rules/security.md` Data Protection.

### Warnings (should fix)
3. **Culture in a file (wrote + reaches)** `ReadingExport.cs:22` (`.Append(r.Value)`) and `:36` (`{reading.Value}` interpolation). Both format the decimal with `CurrentCulture`. `LanguageMiddleware.cs:13-14` sets `CurrentCulture` to `ar-SA` for `x-language: ar`, so Arabic users get `٫` separators in a machine-readable CSV. The change reaches that existing middleware flaw. `ToString("O")` on the dates is culture-safe.
   **Fix**: `r.Value.ToString(CultureInfo.InvariantCulture)` and `string.Create(CultureInfo.InvariantCulture, $"...")`. Separately, the middleware should set the UI culture only. Skill: `localization` §13, bullets 1-2.
4. **CSV escaping / formula injection (wrote)** `ReadingExport.cs:23-24, 36`. `Unit` and `Notes` are written raw. Notes is patient free text, so a comma, quote or newline breaks the rows, and a note starting with `=`, `+`, `-` or `@` runs as a formula when the doctor opens the file in Excel.
   **Fix**: one row writer that quotes every field (doubling any `"` inside) and puts `'` before a leading `= + - @`. Skill: `owasp-aspnetcore` (output encoding).
5. **No range cap or validation (wrote)** `ReadingEndpoints.cs:26`, `ReadingExport.cs:13-16`. `from`/`to` are not validated: `to < from` is accepted, and there is no maximum span. The whole result is loaded with `ToListAsync` and built into one string in memory. A ten-year range is one unbounded query and one large allocation.
   **Fix**: validate the range (e.g. `to > from`, at most 366 days; reject with a 400 ValidationProblem). Stream the response if large exports are expected. Skill: `api-design` §3 and §11 ("A list endpoint without a server-side cap"); `rules/security.md` Input Validation.
6. **Date-range meaning (wrote)** `ReadingEndpoints.cs:26`. The boundaries are bare `DateTime` values. `?from=2026-10-01` means UTC midnight, so a Riyadh patient's readings between 00:00 and 03:00 land on the previous day. The `DateTimeKind` that reaches Npgsql's `timestamptz` comparison (`ReadingsDbContext.cs:20`) is also not pinned down. Npgsql rejects `Unspecified`, so this needs a test.
   **Fix**: take `DateOnly` and convert using the patient's time zone, or take `DateTimeOffset`. Skill: `localization` (time zones), `postgresql-patterns`.
7. **Layer direction (wrote)** `ReadingExport.cs:2-3`. An Application class imports `HomeReadings.Api` (for `ICurrentPatient`) and uses the Infrastructure `ReadingsDbContext` directly. That is Application → API and Application → Infra. It copies the existing `ReadingQueries`, but the change adds a new instance.
   **Fix**: move `ICurrentPatient` to Application (implement it in Api), and read through `ReadingQueries` or a query interface. Rule: `rules/architecture.md` Dependency Rules.
8. **No tests (wrote)**. The change adds two endpoints and no tests, and the repo has no test project. One test would have caught item 1: "patient B requests patient A's reading id on `/export` → 404". It can fail, and it fails today.
   **Fix**: integration tests with `WebApplicationFactory` and Testcontainers PostgreSQL covering ownership, culture (`x-language: ar` produces `.` decimals), escaping and range validation. Skill: `testing-tdd`, `testing-integration`; `rules/testing.md` TDD mandate.

### Suggestions
9. **Duplication / magic string (wrote)** `ReadingExport.cs:18` and `:36`. The header and the row format are written twice, and the two methods read data two different ways (`db` directly vs `queries`). Extract one header constant and one row formatter; that also lets items 3 and 4 be fixed in one place. Skill: `code-quality`.
10. **Response shape (wrote)** `ReadingEndpoints.cs:27, 30`. `"text/csv"` with no charset and no `Content-Disposition`. Arabic notes can show as garbage in Excel without UTF-8 plus a BOM. Use `TypedResults.File(bytes, "text/csv; charset=utf-8", "readings.csv")` or set the header.
11. **XML docs (wrote)** `ReadingExport.cs:11`. Public `AllAsync` has no `<summary>`, `<param>` or `<returns>` (`rules/coding-style.md`).

### Known-traps row: `Program.cs:7` ValidateScopes / ValidateOnBuild — dismissed for this change
The change only adds `AddScoped<ReadingExport>()` (`Program.cs:18`). Its only consumers are request-scoped endpoint handlers. No singleton takes it: `ReadingNotifier` uses `CreateScope()` (`ReadingNotifier.cs:11`). The missing all-environment validation (`dotnet-core` Rules) is pre-existing and this change doesn't reach it. It is counted below.

### Positive
- `AllAsync` filters by `patient.Id` in the SQL, uses `AsNoTracking`, uses the `(PatientId, CollectedAt)` index order, and passes `CancellationToken` through.
- The class is sealed, the DI uses primary constructors, the handlers return `TypedResults`, `StringBuilder` is used in the loop, and the dates use `"O"` (culture-invariant).
- `{id:int}` keeps `/export` from colliding with `/{id}`.

Outside this change: 6.

Not checked: `ReadingService.cs`, `PhotoStore.cs`, `ErrorMiddleware.cs`, `CareTeamClient.cs` and `appsettings.json` were not read, because the change neither calls them nor is called by them. The Roslyn MCP was not available in this tool set, so callers were found with text search; the call graph is small. Nothing was built or run.

For memory: nothing new about the user.