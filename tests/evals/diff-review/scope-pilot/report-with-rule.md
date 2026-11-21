## Code Review: Reading CSV export (diff mode)

### Summary
The change doesn't compile in its current form. The single-reading export also returns any patient's reading to any signed-in user, and the CSV it builds can put wrong numbers in front of a doctor. It also carries two changes the story doesn't need: a notifier batch-size change and a new `priority` field sent to the care team. Every finding below is something the diff **wrote**, **reaches** or **breaks**.

### Critical (must fix)
1. **[Build]** `HomeReadings/Api/ReadingEndpoints.cs:31-35` (wrote): the two `MapGet` calls sit after the closing brace of the class, outside any method, so the file doesn't compile. **Fix**: move them inside `MapReadingEndpoints`, before line 29.
2. **[Build/Startup]** `HomeReadings/Program.cs:45` (wrote): `AddScoped<ReadingExport>()` comes after `public partial class Program;`. That is CS8803, because top-level statements must come before type declarations. Even placed lower it would run after `Build()`/`Run()`, when the service collection is read-only. **Fix**: register it next to `ReadingQueries` (line 17).
3. **[Security – IDOR]** `HomeReadings/Application/ReadingExport.cs:34` (reaches): `OneAsync` calls `ReadingQueries.GetAsync` (`ReadingQueries.cs:12-16`), which filters only on `r.Id == id` and never on the patient. With sequential int ids, any signed-in patient can step through `/readings/{id}/export` and download other patients' glucose values and notes. **Fix**: filter on `r.PatientId == patient.Id` in this query, so someone else's record is NotFound. `GET /readings/{id}` has the same gap; this change adds a second door to it.
4. **[PHI in logs]** `ReadingExport.cs:27` (wrote): it logs the patient's `NationalId` at Information level. A national ID must never reach the logs. **Fix**: log `patient.Id` and the row count only.
5. **[Localization – wrong values to the doctor]** `ReadingExport.cs:22` (`.Append(r.Value)`) and `:36` (`{reading.Value}`) (wrote): both format the value with the current culture, and `LanguageMiddleware.cs:14` sets that to `ar-SA` for Arabic users. The value can come out with an Arabic decimal separator, or with a comma that splits one value into two columns. Either way the doctor reads a wrong or unreadable glucose value. **Fix**: `r.Value.ToString(CultureInfo.InvariantCulture)`. `CollectedAt:"O"` is already culture-safe.
6. **[CSV correctness and injection]** `ReadingExport.cs:24, 36` (wrote): `Notes` is free patient text and goes in unescaped. A comma, quote or newline shifts every later column. A leading `=`, `+`, `-` or `@` runs as a formula when the doctor opens the file in Excel. **Fix**: one CSV writer shared by both methods that quotes and escapes fields (RFC 4180) and neutralises formula-leading characters.

### Warnings (should fix)
1. **[Scope]** `HomeReadings/Application/ReadingNotifier.cs:15, 16, 24, 26` (wrote): it renames `waiting` to `pendingAlerts` and raises `Take(50)` to `Take(100)`. Neither belongs to a CSV export, and the second is a behaviour change: twice as many non-idempotent POSTs to nurses per tick (`CareTeamClient.cs:5`). Move both to their own change.
2. **[Scope / external contract]** `HomeReadings/Infrastructure/CareTeamClient.cs:8, 10` (wrote): adds `priority` for a caller that doesn't exist; the only caller, `ReadingNotifier.cs:19`, never passes it. Every alert now sends `"priority": null` to the care team's system, which changes the request body for an external system. The optional parameter also comes after `CancellationToken`, against CA1068. Remove it from this change.
3. **[Architecture]** `ReadingExport.cs:2-4, 9` (wrote): the Application layer depends on `HomeReadings.Api` (`ICurrentPatient`) and on `ReadingsDbContext` directly. `ReadingQueries` already does the same, but this adds another class that does it. Move `ICurrentPatient` inward and put data access behind a query or repository interface.
4. **[Consistency/SRP]** `ReadingExport.cs:13-16` vs `:34` (wrote): `AllAsync` queries EF directly while `OneAsync` goes through `ReadingQueries`, and the header and row format are written twice. Use one data path and one formatter.
5. **[Performance/Validation]** `ReadingExport.cs:13-16` and `ReadingEndpoints.cs:31` (wrote):
   - There is no check that `from < to` and no cap on the range, so a ten-year request loads the patient's whole history into memory. Validate the range and cap it, or stream the rows.
   - It loads full entities, including `NationalId` and `PhotoPath`. Project to the four columns the CSV needs.
6. **[Dates]** `ReadingEndpoints.cs:31` (wrote): a date-only `from`/`to` (no `Z`) is likely to bind with `Kind=Unspecified`, and Npgsql refuses that for `timestamptz` (`ReadingsDbContext.cs:20`). The request would then return a 500, and `ErrorMiddleware.cs:16` sends `ex.Message` back to the client. Take `DateOnly` or `DateTimeOffset` and convert to a UTC range explicitly. Worth confirming with a test.
7. **[Tests]**: the diff has no tests for the export, ownership, culture or escaping. That goes against the TDD rule. At minimum, add tests for:
   - another patient's id returns 404
   - `ar` + `7.5` gives `7.5`
   - notes containing a comma, quote or `=`
   - the range boundaries

### Suggestions (nice to have)
1. **[HTTP]** `ReadingEndpoints.cs:32, 35`: add `Content-Disposition: attachment; filename=readings.csv` and `text/csv; charset=utf-8`. A UTF-8 BOM would help Excel open Arabic notes correctly.
2. **[Docs]** `ReadingExport.cs:11`: `AllAsync` has no XML doc. Say whether `to` is exclusive.
3. **[Scope, minor]** `.gitignore`: harmless, but unrelated to the story; it fits better in its own commit.

### Positive
- `AsNoTracking()`, `CancellationToken` passed through, and an index-friendly `PatientId` + `CollectedAt` filter on the range export.
- `"O"` round-trip format for timestamps; the class is sealed and uses primary-constructor DI.
- NotFound (not Forbidden) for a missing reading is the right shape, once the query filters by owner.

Older flaws outside the change: 4, not listed.

Not checked: I didn't build or run anything, and the `proven-roslyn` MCP wasn't available, so references were checked with text search. `silent_failures.py` wasn't run either.