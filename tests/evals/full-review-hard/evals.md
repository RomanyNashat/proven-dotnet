# /full-review on a harder fixture — evals

The first fixture (`full-review/PharmacyRefills`) turned out too easy: a plain review with the house
rules found all 12 findings too, so it couldn't show whether the skills earn their context. This one is
built from things a careful reviewer without the skills usually misses, each one a bug the skills' own
tests found in CI. Run it like the first: each case in a fresh subagent, once with proven-dotnet installed
and once without, on `tests/evals/full-review-hard/ClinicReminders`.

All cases are **held out**: nothing in the commands, agents or skills was changed to pass them.

## The planted findings

Each has the skill whose test proves it, so a miss points at what the review didn't load.

| # | Where | What | Expected severity | Proven by |
|---|---|---|---|---|
| H1 | `Program.cs` + `DueReminders` | a singleton holding the scoped `RemindersDbContext`: one context shared by every run and request, and the Production host doesn't validate scopes, so it starts | Critical | `dotnet-core` |
| H2 | `Dockerfile` + SQL Server | `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true` on a service that uses `Microsoft.Data.SqlClient`: it refuses to open a connection in invariant mode. The service starts, and the first query fails | Critical | `sqlserver-patterns`, `localization` |
| H3 | `ReminderService` (static field) | `FindSystemTimeZoneById("Asia/Riyadh")` with no fallback, on an Alpine image without tzdata: the type initializer throws on first use | Critical | `localization` |
| H4 | `ReminderService.SendAsync` | `new CultureInfo("ar-SA")` formats the date the patient reads. Its calendar comes from the machine's ICU (Hijri on one, Gregorian on another), and on this image it throws (invariant mode) | Warning (Critical together with H2's image) | `localization` |
| H5 | `Program.cs` (Quartz) | the 9 a.m. cron trigger has no `InTimeZone`: it fires at 9 UTC, which is noon in Riyadh | Warning | `quartz-scheduling` |
| H6 | `Program.cs` (Quartz) | a persistent store without `ScheduleTriggerRelativeToReplacedTrigger = true`: a run missed during a deploy is silently dropped when the trigger is re-registered at start | Warning | `quartz-scheduling` |
| H7 | `AddHttpClient<SmsClient>` | base address `.../api/v2` without a trailing `/`: the relative `messages` replaces `v2`, so requests go to `/api/messages` | Critical (no SMS is ever sent) | `dotnet-core` |
| H8 | `Program.cs` + `ReminderLog` | `UseSerilog()` replaces the logger factory, so `EnableRedaction` never runs: the classified `Mobile` field silently disappears from the line (no redaction, no value) | Warning | `pii-masking` |
| H9 | `ReminderQueries.PageAsync` | keyset paging on `CreatedAt` alone: reminders created in the same instant (a batch import) are skipped at a page boundary. The index on `(CreatedAt, Id)` is there; the query doesn't use the id | Warning | `efcore-patterns` |
| H10 | `Program.cs` | `Database.MigrateAsync()` at startup | Critical (house ban) | `rules/efcore-rules.md` |
| H11 | `ClinicReminders.csproj` | `Microsoft.Data.SqlClient` 6.1.6 brings `System.Security.Cryptography.Pkcs` 9.0.11 (CVE-2026-70162, CVE-2026-84008): a transitive vulnerability, fixed by pinning 10.0.13 | Critical | `zero-vulns`, `msbuild` |
| H12 | `ReminderService.SendAsync` | `logger.LogError(ex, ...)` on the SMS provider's error: its message can echo the mobile number, and masking doesn't touch exception text | Warning | `pii-masking` |
| H13 | `SendDueRemindersJob` + `SendAsync` | the SMS is sent before `SaveChangesAsync`; a crash between them sends it again on the next run (no idempotency key, no outbox) | Warning | `outbox`, `quartz-scheduling` |
| H14 | `POST /reminders` | `PatientId` comes from the body: any signed-in user schedules reminders (and SMS) for any patient | Critical | `rules/security.md` (ownership) |

**Distractors (must not be reported as problems):**
- D1: `MarkSentAsync` uses `ExecuteUpdateAsync` with a block lambda. That's EF Core 10's form, not a
  mistake.
- D2: `PageAsync` projects to `ReminderRow` without `AsNoTracking()`. A projection to plain values isn't
  tracked; adding it changes nothing.
- D3: `Reference` uses `string.Create(CultureInfo.InvariantCulture, $"...")`: correct for a machine value.
- D4: `ScheduleAsync` adds the audit row in the same `SaveChangesAsync` as the reminder: atomic, which is
  what the audit rule asks for.
- D5: `/health` is `AllowAnonymous`.

## Cases

### Case 1: what only the skills know
**Prompt:** /full-review on `tests/evals/full-review-hard/ClinicReminders`.
**Must:** report H1, H2, H3, H5, H6 and H7, each with the file and the member, and the fix:
`IServiceScopeFactory` or a scoped registration (H1); ICU in the image, invariant mode off (H2); a
fixed-offset fallback or tzdata in the image (H3); `InTimeZone` (H5); the misfire setting (H6); a
trailing `/` (H7).
**Trap:** H2 needs two files read together (the Dockerfile and the package list). A review that reads
only the C# misses it.

### Case 2: data and paging
**Must:** report H9 with the fix (order by `CreatedAt, Id` and put the id in the cursor) and H10.
**Must not:** report D1 or D2.

### Case 3: logs and patient data
**Must:** report H8 (the field vanishes; Serilog as a provider keeps redaction) and H12.
**Must not:** suggest logging the request or the SMS text to diagnose a failure.

### Case 4: the dependency check
**Must:** report H11 as transitive, through `Microsoft.Data.SqlClient`, with the fixed version. If the
scan can't run, say so instead of reporting "no vulnerable packages".

### Case 5: nothing changed
**Must:** leave every file unchanged.

## Scoring

Per run: H1–H14 found (right file and member), severities within one level, D1–D5 not reported, nothing
changed. Report found/14 and distractors flagged/5, with and without proven-dotnet. The point of this
fixture is the gap between the two runs: a gap of 0 on Case 1 means the skills aren't reaching the
review.
