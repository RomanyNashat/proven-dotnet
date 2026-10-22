## Security Review (diff): feat/export into main, /tmp/claude-0/diffeval/run-3

I read every changed file in full, plus the code the change calls or relies on: ReadingQueries, CurrentPatient, Reading, ReadingsDbContext, LanguageMiddleware, ErrorMiddleware, ReadingService, appsettings.json and the csproj.

### Critical
1. **A01 IDOR: one patient can export another patient's reading.** Type: *reaches*. The new `GET /readings/{id:int}/export` (`HomeReadings/Api/ReadingEndpoints.cs:29-30`) calls `ReadingExport.OneAsync` (`HomeReadings/Application/ReadingExport.cs:34`). That calls the existing `ReadingQueries.GetAsync`, which filters only on `r.Id == id` (`HomeReadings/Application/ReadingQueries.cs:14`) and never checks `PatientId`. IDs are sequential ints, so any signed-in patient can walk `/readings/1/export`, `/2/export`, ... and get every patient's glucose value, unit and notes as CSV. The change adds a second door onto a lookup with no ownership check, and calls it a "share" feature.
   **Fix:** filter by owner in SQL, so another patient's record is a 404. Either do it in `OneAsync` with its own query (`.Where(r => r.Id == id && r.PatientId == patient.Id)`), or fix `GetAsync` itself so both endpoints are covered. Add a test where patient B asks for patient A's id and gets 404. Rule: `rules/security.md` (every lookup by ID checks ownership). Skill: `auth-patterns` section 4.

### Warning
2. **A09 PII in logs: national ID logged in clear.** Type: *wrote*. `ReadingExport.cs:27` logs `{NationalId}` on every export. appsettings.json sets up no Serilog masking (no destructuring policy, no masking operators), so the full national ID reaches the log sink. To get this one value, the query also loads whole entities (`ReadingExport.cs:13-16`), including `NationalId` and `PhotoPath`.
   **Fix:** log `patient.Id`, the date range and the row count, never `NationalId`. Project to only the CSV columns (`Select(r => new { r.CollectedAt, r.Value, r.Unit, r.Notes })`). If this line is meant as the PHI-access audit record, write it as a proper audit row. Skills: `pii-masking`, `healthcare-compliance` ("Reads aren't changes": record PHI reads).

3. **A05 CSV and formula injection in a file that goes to a doctor.** Type: *wrote*. At `ReadingExport.cs:21-24` and `:36`, `Notes` and `Unit` come from the patient and are written with no quoting or escaping. A note containing a comma or newline adds columns or fake reading rows to a clinical record. A note starting with `=`, `+`, `-` or `@` runs as a formula when the doctor opens the file in Excel (for example `=HYPERLINK(...)`). Combined with finding 1, an attacker can also pull other patients' notes into a CSV.
   **Fix:** one `CsvField(string?)` helper used by both methods. It wraps each field in double quotes and doubles any quote inside. If the field starts with `=`, `+`, `-`, `@`, tab or CR, it prefixes `'`. Skill: `owasp-aspnetcore` (injection). The skill has no CSV sample, so this fix isn't tested there.

4. **A08 integrity: glucose values change with the caller's language.** Type: *wrote*. `.Append(r.Value)` (`ReadingExport.cs:22`) and `{reading.Value}` (`:36`) format with `CurrentCulture`. `LanguageMiddleware.cs:14` sets that to `ar-SA` when `x-language: ar` is sent, so the value is written with Arabic separators or digits. A comma decimal separator would also split the column. Either way the number the doctor reads is wrong.
   **Fix:** `r.Value.ToString(CultureInfo.InvariantCulture)` in both places. Rules: `csharp-standards.md` (InvariantCulture, never let ar-SA become the formatting culture). Skill: `localization`. This one overlaps with code review.

5. **A10 / A06: unvalidated date range, and errors leak to the client.** Type: *reaches*. At `ReadingEndpoints.cs:26`, `from` and `to` are bound as `DateTime` from the query string. A date sent without an offset arrives with `Kind=Unspecified`. Npgsql throws for that against `timestamptz` (`postgresql-patterns` section 2, tested). The existing `ErrorMiddleware.cs:16` then returns `ex.Message` and the inner exception's message as the response body. So the new endpoint gives anyone an easy way to make the service show internal error text. The range also has no limit (`from > to`, years of data), and the whole result is loaded into memory with `ToListAsync`.
   **Fix:** take `DateOnly` or `DateTimeOffset`, convert to UTC, and reject `from >= to` or a range over a set maximum (say 366 days) with a 400 ProblemDetails. Separately, ErrorMiddleware should return ProblemDetails with a trace id and nothing internal. Skills: `postgresql-patterns`, `owasp-aspnetcore`.

### Suggestion
6. **A02:** the CSV responses (`ReadingEndpoints.cs:27`, `:30`) contain PHI but send no `Cache-Control: no-store` and no `Content-Disposition: attachment; filename=...`. Add both, so proxies or the device don't cache the data. Type: *wrote*.
7. **Known-traps item `Program.cs:7` (ValidateScopes/ValidateOnBuild):** confirmed as a fact, but dismissed as a security finding for this change. The new registration (`Program.cs:18`, `AddScoped<ReadingExport>`) depends only on scoped or singleton-safe services. No singleton captures it: `ReadingNotifier` uses `IServiceScopeFactory`. That makes this a DI hygiene point (`dotnet-core`) for the code reviewer, not a vulnerability the change adds.

Checks with nothing to report:
- **Auth:** the new endpoints sit in the `/readings` group, so they inherit `RequireAuthorization()`.
- **Steering text:** none found in the changed files.
- **Packages:** the csproj adds none. I did not run `dotnet list package --vulnerable`.

Outside this change: 8

Not checked: the dependency vulnerability scan (no command was run), the rest of `ReadingNotifier`, `CareTeamClient` and `PhotoStore` beyond how they relate to the change, and runtime behaviour (nothing was built or run). The Kind=Unspecified failure in finding 5 comes from the skill's tested note, not from running this code.