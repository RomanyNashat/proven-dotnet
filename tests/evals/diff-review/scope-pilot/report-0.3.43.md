## Code Review: Reading CSV export (diff mode)

### Summary
The export doesn't compile yet. Once it does, the single-reading export lets any patient download another patient's reading. Two more problems are also new in this change: the patient's national ID gets written to the logs, and the CSV is written in a way that can show the doctor wrong values. Two edits are outside the story (the notifier batch size and the alert `priority`) and should go in a separate MR.

### Critical (must fix)
1. **Build: wrote** `HomeReadings/Api/ReadingEndpoints.cs:31-35`: the hunk was inserted after the class's closing brace at line 30, so the two `MapGet` calls sit outside any method. `HomeReadings/Program.cs:45` has the same kind of problem: `AddScoped<ReadingExport>()` comes after `public partial class Program;`, which is error CS8803. It also comes after `Build()` and `Run()`, so even if it compiled it would be too late to register anything.
   **Fix**: move the endpoints inside `MapReadingEndpoints` and move the registration up next to `AddScoped<ReadingQueries>()` at line 17.
2. **Security / IDOR: wrote a new endpoint that reaches an old flaw** `ReadingExport.cs:59` → `ReadingQueries.cs:12-16`: `OneAsync` calls `GetAsync(id)`, which filters only by `r.Id == id`, with no `PatientId == patient.Id`. Ids are sequential ints, so any signed-in patient can download anyone's reading as `/readings/{n}/export`.
   **Fix**: add `&& r.PatientId == patient.Id` in `GetAsync` and return NotFound otherwise. This also closes the same hole on `GET /readings/{id}`. Add a test where patient A asks for patient B's reading and gets 404.
3. **PHI in logs: wrote** `ReadingExport.cs:52`: the national ID is logged on every export. The house rule is that PII is masked in logs and never logged in clear.
   **Fix**: log `patient.Id` and the row count only.
4. **Wrong data reaches the doctor: wrote** `ReadingExport.cs:46-49,61`: `Notes` and `Unit` are written without quoting or escaping. A note containing `,` or a newline shifts every column after it, so the doctor reads the wrong value or unit. A note starting with `=`, `+`, `-` or `@` runs as a formula when the doctor opens the file in Excel (CSV injection).
   **Fix**: quote each field per RFC 4180 (double any `"` inside it), prefix formula-leading characters with `'`, and add a test with a note like `"=1+1, see\nme"`.
5. **Localization: wrote, reaching an old flaw** `ReadingExport.cs:47,61`: `Append(r.Value)` and `{reading.Value}` format with the current culture. `LanguageMiddleware.cs:14` sets that to `ar-SA` for Arabic users, so the decimal separator can come out as Arabic (`٫`) depending on the machine's ICU. A glucose value in a file meant for another system has to be invariant. (`:O` on the date is already invariant.)
   **Fix**: use `r.Value.ToString(CultureInfo.InvariantCulture)` or `string.Create(CultureInfo.InvariantCulture, …)`.

### Warnings (should fix)
1. **Input validation: wrote** `ReadingEndpoints.cs:31`: there is no validator on `from`/`to`. A reversed range just returns an empty file, and an unbounded range loads the patient's whole history into memory (`ReadingExport.cs:38-41`). Validate that `from < to`, cap the span (for example 1 year), and stream or limit the rows.
2. **Dates: wrote (plausible, not run)** `ReadingEndpoints.cs:31`: `?from=2026-11-01` binds as `DateTimeKind.Unspecified`. Npgsql refuses to compare that with a `timestamptz` column, so it throws and the old `ErrorMiddleware` sends back a 500 containing `ex.Message`. Take `DateTimeOffset`, or a `DateOnly` converted to UTC.
3. **Layering: wrote** `ReadingExport.cs:2-3,38`: a new Application class references `HomeReadings.Api` (`ICurrentPatient`) and queries `ReadingsDbContext` directly. It copies the existing `ReadingQueries` pattern, but new code shouldn't add to it. It also splits responsibility: `AllAsync` runs its own query while `OneAsync` delegates, which is how the ownership filter got lost. Put both queries in one place, behind an interface Application owns, and add a projection.
4. **Tests: wrote** none. The TDD rule asks for failing tests first. At minimum add: export another patient's reading → 404; a note containing a comma or a formula; output under `ar-SA` culture; a reversed or empty range.
5. **Scope / external contract: wrote** `CareTeamClient.cs:8,10`: `priority` is added after the `CancellationToken` (the token should stay last), and no caller passes it (`ReadingNotifier.cs:19`). Every alert now sends `"priority": null` to a system with no idempotency, and nothing in the story asks for that. Remove it, or ship it in its own MR once the care team confirms they accept the field.
6. **Scope: wrote** `ReadingNotifier.cs:15`: the batch size goes from 50 to 100 with no reason given, unrelated to the export. Doubling the work per tick of a loop that makes one POST per alert should be a separate, explained change.

### Suggestions (nice to have)
1. **Magic values** `ReadingExport.cs:43,61`, `ReadingNotifier.cs:15`: put the repeated CSV header and the `100` in named constants (or config for the batch size).
2. **Projection** `ReadingExport.cs:38`: select just the four exported columns instead of whole entities. That stops pulling `NationalId` into memory at all.
3. **Response** `ReadingEndpoints.cs:32,35`: add `Content-Disposition: attachment; filename=readings-….csv` and a UTF-8 BOM, so Arabic notes open correctly in Excel.
4. **Docs** `ReadingExport.cs:36`: `AllAsync` has no XML doc, and the range end is exclusive (`< to`), which is worth stating.

### Positive
- `AllAsync` filters by the current patient, uses `AsNoTracking()`, and orders in the database.
- `CancellationToken` is passed all the way through. The class is sealed and uses a primary constructor. The CSV is built with `StringBuilder`, not concatenation in the loop.
- The endpoints inherit the group's `RequireAuthorization()`, and the missing-reading case returns `NotFound`.
- `pendingAlerts` reads better than `waiting`.

**Older flaws the change doesn't touch: 8, not listed.** They are in `ErrorMiddleware`, the shared cache key in `LatestAsync`, `ReadingService`, `ReadingsDbContext`, the POST retries, and the notifier loop.

**Not checked:** I didn't build or run anything, so the Npgsql `Kind` behaviour in Warning 2 comes from reading the code, not from a run. `proven-roslyn` wasn't available, so callers (for example, the only caller of `SendAlertAsync`) were found by reading the files in this small project. I didn't run `silent_failures.py`; reading by hand, the change adds no catch or fallback.