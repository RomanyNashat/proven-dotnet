## Code Review: feat/export → main (HomeReadings CSV export), code-reviewer

### Summary
The change adds two endpoints and an Application class that turn a patient's readings into CSV. The single-reading export goes through a query with no ownership check, so any logged-in patient can download anyone's glucose readings by guessing the id. The new code also logs the patient's national ID, and it writes the free-text `Notes` straight into the CSV. **Don't merge until the three Critical items are fixed.**

### Critical (must fix)
1. **Security: IDOR on health data.** `Api/ReadingEndpoints.cs:29-30` and `Application/ReadingExport.cs:59`. *Reaches.* `OneAsync` calls `ReadingQueries.GetAsync`, which filters by `r.Id == id` only (`ReadingQueries.cs:12-16`). Ids are sequential ints, so `/readings/{n}/export` hands any reading to any caller. `ReadingExport` injects `ICurrentPatient` but `OneAsync` never uses it.
   **Fix:** add `r.PatientId == patient.Id` to the query, and so return NotFound for someone else's reading. Fixing it in `GetAsync` also closes the existing `GET /{id}`. Add a test where patient B gets 404 for patient A's id. Sources: `api-design` §11; `rules/security.md` "Every lookup by ID checks ownership".
2. **Security: PII in logs.** `ReadingExport.cs:52`. *Wrote.* It logs `NationalId` at Information, and Serilog sends that to the console and on to the log store. It is also wrong data: it takes the first row's ID, and logs null when the export is empty.
   **Fix:** log `patient.Id` and the row count, never `NationalId`. Source: `rules/security.md` Data Protection.
3. **Correctness/security: CSV not escaped, formula injection.** `ReadingExport.cs:46-49` and `:61`. *Wrote.* `Notes` (patient free text) and `Unit` go into the CSV raw:
   - A comma, quote or newline in a note shifts the columns, so the doctor reads a value under the wrong heading.
   - A note starting with `=`, `+`, `-` or `@` runs as a formula when the file is opened in Excel.

   **Fix:** use one `ReadingCsv` writer for both paths. Quote each field per RFC 4180 (wrap it in `"` and double any inner `"`), and put `'` in front of a leading `= + - @ \t \r`.

### Warnings (should fix)
4. **Localization: values follow the request culture.** `ReadingExport.cs:47` (`.Append(r.Value)`) and `:61` (`{reading.Value}`). *Wrote and reaches.* `LanguageMiddleware.cs:13-14` sets `CurrentCulture` to `ar-SA` when the header is `x-language: ar`. Depending on the machine's ICU, a glucose value can then come out with an Arabic decimal separator in a file meant for a machine. `CollectedAt:O` is invariant and fine. **Fix:** `r.Value.ToString(CultureInfo.InvariantCulture)` and `string.Create(CultureInfo.InvariantCulture, ...)`. Sources: `localization` §1 and §13; `rules/csharp-standards.md`.
5. **API: unchecked export range.** `ReadingEndpoints.cs:26` and `ReadingExport.cs:38-41`. *Wrote.*
   - Nothing checks that `from < to`, and there is no cap on the range or the row count. Every row is loaded into memory and built into one string.
   - Likely bug, not run: query-bound `DateTime`s with no offset arrive as `Kind=Unspecified`, and Npgsql refuses those against a `timestamptz` column. The result is a 500, and the existing `ErrorMiddleware` (`:16`) then sends `ex.Message` back to the client.

   **Fix:** validate the range and set a maximum span (for example 90 days). Take `DateTimeOffset` or force UTC. Add a test that calls the endpoint with `?from=2026-10-01&to=2026-10-31`. Source: `api-design` §11.
6. **Architecture: Application references Api and Infrastructure.** `ReadingExport.cs:2-3`. *Wrote.* This copies the existing `ReadingQueries` pattern. **Fix:** move `ICurrentPatient` into Application and put data access behind an interface. Source: `rules/architecture.md`.
7. **Single responsibility and data loaded.** `ReadingExport.cs:34`. *Wrote.*
   - The class has two data paths (`db` and `queries`) and two separate CSV builders.
   - `AllAsync` loads whole entities, including `NationalId` and `PhotoPath`. Project to `ReadingView` instead, so the PII is never read.
8. **Tests: none in the change.** *Wrote (missing).* Two endpoints and a new class arrive with no tests, against the `rules/testing.md` TDD mandate. Tests needed: ownership 404, a comma or `=` in a note, `x-language: ar` output, and a range with no offset.

### Suggestions
9. **Magic strings and docs.** `ReadingExport.cs:36,43,61` and `ReadingEndpoints.cs:27,30`. *Wrote.* Make the header row and `"text/csv"` constants. `AllAsync` has no XML doc.
10. **Download handling.** `ReadingEndpoints.cs:27,30`. *Wrote.* There is no `Content-Disposition: attachment; filename=...`. If doctors open the file in Excel, Arabic notes need UTF-8 with a BOM. Set the encoding explicitly.

### Known trap: Program.cs:7 scope validation is dismissed for this change
The change only adds `AddScoped<ReadingExport>()` (`Program.cs:18`). That is the right lifetime, its dependencies are scoped or singleton, and no singleton consumes it. Turning the checks on would catch nothing this change does. The missing `ValidateScopes`/`ValidateOnBuild` setting is older than this change (`dotnet-core`), so it is counted below.

### Positive
- The group keeps `RequireAuthorization()`, and `AllAsync` filters by the caller with `AsNoTracking`.
- The export class is `sealed`, uses a primary constructor, passes the `CancellationToken` all the way down, and uses `TypedResults`.
- `.gitignore` for `bin/` and `obj/` is a sensible addition.
- No text in the repo is addressed to AI reviewers.

Outside this change: 16

Not checked: no build or run, so the DateTime Kind failure in #5 is inferred, not run. Roslyn MCP wasn't available here, so callers were traced with Read. I did not open `CareTeamAlert.cs`, `CareTeamClient.cs` or `Messages.cs`; nothing in the change calls them.