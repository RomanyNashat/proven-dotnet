## Code Review: feat/export into main (HomeReadings CSV export), code-reviewer

### Summary
The CSV export works on the happy path. But the single-reading export lets any signed-in patient read anyone else's reading, the bulk export writes a patient's national ID to the logs, and the CSV is built by hand with no escaping and with culture-dependent number formatting. The change has no tests.

### Critical (must fix)
1. **Security / IDOR (reaches)** `HomeReadings/Api/ReadingEndpoints.cs:29-30` → `Application/ReadingExport.cs:34` → `Application/ReadingQueries.cs:12-16`. The new `GET /readings/{id}/export` calls `ReadingQueries.GetAsync`, which filters only on `r.Id == id`, never on `patient.Id`. Ids are sequential ints, so a patient can walk `/readings/1/export`, `/2/export` and so on and download other patients' glucose values and notes (health data). The change adds a new endpoint over an existing query that has no ownership check.
   **Fix:** add `&& r.PatientId == patient.Id` to `GetAsync`, so another patient's reading comes back null and the endpoint returns 404. This also closes the same hole on the existing `GET /{id}`. Add a test where patient B asks for patient A's reading and expects NotFound. Rules: `api-design` §11 (first item), `rules/security.md` ("Every lookup by ID checks ownership").
2. **PII in logs (wrote)** `Application/ReadingExport.cs:27`. `logger.LogInformation("Export for {NationalId}…", rows.FirstOrDefault()?.NationalId, …)` writes a national ID next to health activity into Elastic in plain text. It is also unreliable: the value is null when there are no rows.
   **Fix:** log `patient.Id` (or nothing that identifies the person) and the row count. Rules: `pii-masking`, `rules/security.md` Data Protection ("never log … health records").

### Warnings (should fix)
1. **Localization (wrote; reaches `LanguageMiddleware.cs:14`)** `ReadingExport.cs:22` `.Append(r.Value)` and `:36` `{reading.Value}` format the decimal with `CurrentCulture`. `LanguageMiddleware` sets that to `ar-SA` when `x-language: ar`, so the export can say `6٫5` instead of `6.5`, and with a comma-decimal culture the separator collides with the CSV delimiter. The `:O` dates are culture-invariant, so they are fine.
   **Fix:** `r.Value.ToString(CultureInfo.InvariantCulture)` in both places. Rule: `localization` §1 ("API values are never localized") and §13 item 1.
2. **CSV correctness and injection (wrote)** `ReadingExport.cs:21-24, 36`. `Notes` and `Unit` are free text written raw. A comma, quote or newline in a note shifts columns or adds rows. A note starting with `=`, `+`, `-` or `@` runs as a formula when the doctor opens the file in Excel.
   **Fix:** one `CsvField` helper that wraps fields in quotes, doubles inner quotes, and prefixes formula characters with `'`. Or use CsvHelper. Rule: `owasp-aspnetcore` (output encoding).
3. **Unbounded, unvalidated range (wrote)** `ReadingEndpoints.cs:26` / `ReadingExport.cs:13-16`. `from` and `to` are not validated (`from > to`, a ten-year span), there is no row cap, and the whole result is materialised as entities and then held as one string.
   **Fix:** validate the pair (for example, at most 366 days, `from < to`) and return 400 otherwise. Project to the four columns with `Select` instead of loading full `Reading` entities, which also stops pulling `NationalId` and `PhotoPath` into memory. Rule: `api-design` §11 ("A list endpoint without a server-side cap"), `rules/performance.md`.
4. **DateTime binding against timestamptz (wrote; needs a check)** `ReadingEndpoints.cs:26`. A query like `?from=2026-10-01` binds a `DateTime` with Kind Unspecified. Npgsql refuses Unspecified for `timestamptz` (`ReadingsDbContext.cs:20`), so the request throws and the existing `ErrorMiddleware` sends `ex.Message` back to the client. I did not run it; one integration test settles it.
   **Fix:** bind `DateOnly` or `DateTimeOffset` and convert to UTC explicitly.
5. **Layering (wrote)** `ReadingExport.cs:2-4, 9`. The new Application class imports `HomeReadings.Api` (`ICurrentPatient`) and `HomeReadings.Infrastructure` (`ReadingsDbContext`), so Application depends on API and Infrastructure. It copies the existing `ReadingQueries` shape, but this change wrote the new dependency. Rule: `rules/architecture.md` Dependency Rules.
6. **Two patterns in one class (wrote)** `ReadingExport.cs:13` vs `:34`. `AllAsync` queries the DbContext directly, while `OneAsync` goes through `ReadingQueries`. The CSV header string is repeated (`:18`, `:36`) and the row formatting is written twice, once with `StringBuilder` and once with interpolation, so the two exports can drift (they already differ in escaping risk).
   **Fix:** one query path, a `const` header, and one row formatter.
7. **Tests (wrote)**. No test came with the change, which breaks the TDD mandate (`rules/testing.md`, `testing-tdd`). At minimum, add tests for: another patient's id → 404, a note with a comma or quote is escaped, `x-language: ar` still gives an invariant decimal, and a bad range → 400.

### Suggestions
1. `ReadingEndpoints.cs:27, 30`: send `text/csv; charset=utf-8` with a `Content-Disposition: attachment; filename=…`. Excel misreads UTF-8 Arabic notes without a charset or BOM.
2. `ReadingExport.cs:11`: the public method `AllAsync` has no XML doc (`rules/coding-style.md`).

### Known-traps row: `Program.cs:7` ValidateScopes/ValidateOnBuild
Dismissed for this change. Its only edit there, `Program.cs:18` `AddScoped<ReadingExport>()`, is scoped and depends only on scoped services (DbContext, `ICurrentPatient`, `ReadingQueries`) plus `ILogger`, so it adds no captive dependency. The missing validation is a flaw that already existed, outside this change (`dotnet-core`, Rules).

### Positive
- `AllAsync` filters by `patient.Id`, uses `AsNoTracking`, and passes the `CancellationToken` through.
- It returns TypedResults with a correct `Task<IResult>` NotFound branch, and the class is `sealed` with a primary constructor.
- `.gitignore` for `bin/` and `obj/` is fine.

Not checked: whether it builds or runs (I ran nothing), and the security and DBA reviewers' areas beyond what is listed above.

Outside this change: 9.