# /full-review — evals

Mechanical checks only (see `skill-evals`): does the review find what's planted, rank it right, and leave
the distractors alone. Prose quality isn't graded. Run each case in a fresh subagent, once with
proven-dotnet installed and once without, on `tests/evals/full-review/PharmacyRefills`.

All cases are **held out**: the command and its agents were written before this fixture existed and
haven't been changed to pass it.

## The planted findings

| # | Where | What | Expected severity |
|---|---|---|---|
| F1 | `RefillQueries.SearchAsync` | SQL built by interpolating `medication` into a Dapper query: injection | Critical |
| F2 | `GET /refills/{id}` | returns any patient's refill to any signed-in user: no ownership check (IDOR) | Critical |
| F3 | `POST /refills/{id}/dispense` | any signed-in user can dispense any refill: no ownership or pharmacist policy | Critical |
| F4 | `RefillService.RequestAsync` | logs the national id: PHI in logs | Critical (Warning acceptable) |
| F5 | `RefillService.NotifyInsurer` | `.Result` on a task, `new HttpClient()`, a hard-coded URL, and `catch (Exception) { }` | Critical for `.Result` and the swallow; Warning for the rest |
| F6 | `RequestAsync` + `NotifyInsurer` | the insurer call after `SaveChanges` is a dual write: a crash or failure loses it silently | Warning |
| F7 | `Refill.PharmacistNotes` | string with no `HasMaxLength`: an unbounded `text` column | Warning |
| F8 | `RefillQueries.CountsPerPatientAsync` | one query per patient in a loop (N+1) | Warning |
| F9 | `PharmacyRefills.csproj` | `System.Text.Json` 8.0.4, a direct dependency with a known high-severity vulnerability (fixed in 8.0.5) | Critical |
| F10 | `GET /refills/{id}` | returns the entity, so `PharmacistNotes` leaks to the caller; use a response DTO | Warning |
| F11 | `RequestRefill` | no validator on the request | Warning |
| F12 | `DispenseAsync` | `KeyNotFoundException` for a missing refill becomes a 500, not a 404 | Suggestion or Warning |

**Not planted, but real** (found by the first runs, 2026-10-10; count them as found, never as noise):
- R1: the Dapper SQL uses `patient_id`/`medication_name`, but the EF model has no snake_case convention,
  so its columns are `PatientId`/`MedicationName`. The search fails at runtime either way.
- R2: `AddJwtBearer()` needs the `Microsoft.AspNetCore.Authentication.JwtBearer` package, which the
  project doesn't reference, so it doesn't compile.

**Distractors (must not be reported as problems):**
- D1: `RefillQueries.ForPatientAsync` uses `FromSql($"... {patientId}")`. That's a `FormattableString`:
  EF Core sends it as a parameter. Not injection.
- D2: `/health` is `[AllowAnonymous]`: a health endpoint is meant to be.
- D3: `RefillService` takes `TimeProvider`: correct, not a finding.

### Case 1: the security findings
**Prompt:** /full-review on `tests/evals/full-review/PharmacyRefills`.
**Must:** report F1, F2 and F3 as Critical, each naming the file and the member; for F2 and F3 say the
fix is an ownership check (query filtered by the caller's patient, or a resource-based authorization
handler) and, for F3, a pharmacist policy.
**Must not:** report D1 as SQL injection.
**Trap:** F1 and D1 both put a variable inside `$"..."` SQL. Telling them apart is the pass.

### Case 2: PHI and the swallowed failure
**Must:** report F4 (naming `NationalId` as the PHI), and F5's `.Result` and empty catch.
**Must:** report F6 as a lost-message risk and point to an outbox (or a durable retry), not only "add a log".
**Must not:** suggest logging the exception *with* the request body (that would log the national id again).

### Case 3: the database review
**Must:** report F7 against the column rules (bounded strings), and F8 as N+1 with a single grouped
query as the fix (`GroupBy` + `Count`, or one SQL `GROUP BY`).
**Must not:** flag `FindAsync` tracking as Critical (at most a Suggestion).

### Case 4: the dependency check
**Must:** report F9 as Critical: package, resolved version 8.0.4, direct, the advisory (CVE-2024-43485,
GHSA-8g4q-xg66-9fp4, high), and the fixed version (8.0.5). If `dotnet list package --vulnerable` can't run in the environment, say so
and fall back to reading the project file, rather than reporting "no vulnerabilities".
**Must not:** change the project file (the command is read-only).
**Trap:** with no network the tool returns nothing; "no vulnerable packages found" is a fail.

### Case 5: coverage with no tests
**Must:** say there's no test project, so line and branch coverage are 0% (or "not measurable") against
the 80% / 70% targets, and point to `/coverage`.
**Must not:** invent a coverage percentage, or write a test.

### Case 6: nothing changed
**Must:** leave every file in the fixture unchanged (check with `git status`).
**Trap:** fixing F1 "while it's there". The review is read-only; a fix is a fail.

## Scoring

Per run: F1–F12 found (with the right file and member), severities within one level of the table, D1–D3
not reported, nothing changed. Report found/12, distractors wrongly flagged/3, and any change to the
fixture as an automatic fail. Compare with-proven against without-proven; if they're the same, the
review agents aren't earning their context.

Results per run are in `results.md`.
