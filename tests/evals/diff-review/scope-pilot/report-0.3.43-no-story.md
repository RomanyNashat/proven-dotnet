## Code Review: HomeReadings, CSV export of readings (diff mode)

### Summary
Do not merge. The change does not compile in two places. The new single-reading export lets any signed-in patient download another patient's reading. Every export also writes the patient's national ID to the log. Every finding below is something the change wrote (**wrote**) or something it relies on (**reaches**).

### Critical (must fix)
1. **Build (wrote)** `HomeReadings/Api/ReadingEndpoints.cs:31-35`: the two new `MapGet` calls sit after the class's closing brace at line 30, so `readings` is out of scope and the file won't compile.
   **Fix**: move them inside `MapReadingEndpoints`, before line 29.
2. **Build (wrote)** `HomeReadings/Program.cs:45`: `AddScoped<ReadingExport>()` comes after `public partial class Program;`. Top-level statements can't follow a type declaration (CS8803). The line also comes after `builder.Build()` (line 33), where the service collection is read-only.
   **Fix**: put it next to the other registrations at lines 16-17.
3. **Security / IDOR (wrote + reaches)** `HomeReadings/Application/ReadingExport.cs:59`: `OneAsync` calls `ReadingQueries.GetAsync`, which filters only on `r.Id == id` (`ReadingQueries.cs:14`). So `GET /readings/{id}/export` returns any patient's reading for a guessed int id.
   **Fix**: filter on `r.PatientId == patient.Id` in the export's own query, or add that filter to `GetAsync`. Changing `GetAsync` also closes the same hole in the existing `GET /readings/{id}`.
4. **PII in logs (wrote)** `ReadingExport.cs:52`: every export logs `{NationalId}` in plain text, which the security rules forbid.
   **Fix**: log `patient.Id` and the row count only. Project the query (see Suggestion 2) so `NationalId` isn't loaded at all.
5. **Culture, wrong value sent to a doctor (wrote + reaches)** `ReadingExport.cs:47` and `:61`: `Append(r.Value)` and `{reading.Value}` format with the current culture. `LanguageMiddleware.cs:14` sets `CurrentCulture` to `ar-SA` when the header is `x-language: ar`. Arabic-language patients then get values like `12٫5` (Arabic decimal separator) in a file the doctor's tool parses. The `"O"` dates are invariant and fine.
   **Fix**: use `r.Value.ToString(CultureInfo.InvariantCulture)` and `string.Create(CultureInfo.InvariantCulture, $"…")`.

### Warnings (should fix)
1. **CSV / formula injection (wrote)** `ReadingExport.cs:49`, `:61`: `Notes` is free text from the patient and goes out unescaped. A comma adds columns. A newline adds a fake reading row. A leading `=`, `+`, `-` or `@` runs as a formula in the doctor's spreadsheet. Quote every field per RFC 4180, double any embedded quotes, and prefix formula-leading cells with `'`.
2. **Input validation (wrote)** `ReadingEndpoints.cs:31`: `from` and `to` are not validated, so there's no `from < to` check and no maximum range. One request can pull a patient's whole history into memory. Query values without an offset probably bind as `Kind=Unspecified`, which Npgsql rejects for `timestamptz`. That 500 then **reaches** `ErrorMiddleware.cs:16`, which returns `ex.Message` to the client. Take `DateOnly` or `DateTimeOffset`, convert to UTC, cap the range and return 400 when it's out of bounds.
3. **Layering (wrote)** `ReadingExport.cs:27-28`, `:34`: the new Application class imports `HomeReadings.Api` (for `ICurrentPatient`) and queries `ReadingsDbContext` directly. It copies the pattern in `ReadingQueries`, but that doesn't make it right for new code. Move `ICurrentPatient` into Application, and do the reads behind an interface or in the query class.
4. **CareTeamClient payload change (wrote)** `CareTeamClient.cs:8`, `:10`: the new optional `priority` parameter sits after `CancellationToken` (CA1068). Nothing passes it. It still adds `"priority": null` to every alert sent to an external system that the class comment calls non-idempotent. If that API rejects unknown or null fields, every alert fails. Remove it until a caller needs it, then make it an enum before `ct`.
5. **Batch doubled onto an N+1 (reaches)** `ReadingNotifier.cs:15`: `Take(50)` became `Take(100)` inside a commit that is otherwise a rename. The loop runs `SingleAsync`, an HTTP POST and `SaveChangesAsync` once per alert, so the change doubles that per-tick work. Name the batch size as a constant, split the behaviour change from the rename, and load the readings with one `Where(r => ids.Contains(r.Id))`.
6. **Tests (wrote)**: there are no tests for the new feature. The TDD rule requires at least: another patient's id returns 404, an `ar` culture header still gives invariant numbers, notes with a comma or newline are escaped, and an empty range returns the header only.

### Suggestions (nice to have)
1. **Duplication** `ReadingExport.cs:43-49` / `:61`: the header and row format are written twice. Use one `WriteRow` helper and one header constant.
2. **Performance** `ReadingExport.cs:38-41`: the query loads whole entities. Project only the four columns it needs, or stream them with `AsAsyncEnumerable` into the response.
3. **Design** `ReadingExport.cs:34`: the class takes both `db` and `ReadingQueries`. Use one way of reading data.
4. **API** `ReadingEndpoints.cs:31-35`: add `Content-Disposition` with a filename. The group now has 7 endpoints, which is the house maximum.
5. **Docs** `ReadingExport.cs:36`: `AllAsync` has no XML doc. Also consider an audit record for PHI exports (see `healthcare-compliance`).

### Positive
- `AllAsync` filters by `patient.Id` and uses `AsNoTracking()`, and the CancellationToken is passed all the way through.
- Dates use the `"O"` format, which doesn't depend on culture. The class is `sealed` and uses a primary constructor.
- `pendingAlerts` reads more clearly than `waiting`. Adding `bin/` and `obj/` to `.gitignore` is right.

Seven issues the change didn't write or reach are not listed. One is that the `LatestAsync` cache key is shared by all patients.

Not checked: I didn't build or run anything. The `proven-roslyn` MCP isn't available in this session, so callers were traced by reading the files.