# /full-review: ClinicReminders (consolidated from the scan, code-reviewer, security-reviewer, dba-reviewer)

## Critical
1. **Singleton `DueReminders` holds a scoped `RemindersDbContext`** (`Program.cs:20`, `DueReminders.cs`, `SendDueRemindersJob.cs:16`). The root context tracks the loaded reminders; `MarkSent` changes them; the job saves its own scoped context, which tracks nothing, so `SentAt` is never saved and every due reminder is re-sent every run. Fix: make it scoped or delete it and query in the job; turn on `ValidateScopes` + `ValidateOnBuild` in every environment. (scan, all three reviewers; `dotnet-core`)
2. **Send-then-save with no idempotency key** (`SendDueRemindersJob.Execute`, `ReminderService.SendAsync`). All SMS go first, one `SaveChanges` at the end; a crash, a pod kill, or a timeout (`TaskCanceledException`, not caught by `catch (HttpRequestException)`) re-sends everything already sent. Fix: claim each row with one conditional `ExecuteUpdateAsync` (`WHERE Id = @id AND SentAt IS NULL [AND ClaimedAt IS NULL]`) before sending, skip if 0 rows; then send; then record. On a provider error status release the claim; on a timeout the outcome is unknown, leave it claimed and log. No retry on this POST (`Retry.DisableForUnsafeHttpMethods()` if a resilience handler is added). Never put the HTTP call inside a transaction. (code, dba; `outbox`, `polly-resilience`)
3. **The app creates the schema** (`Program.cs:43-46`, `EnsureCreatedAsync`). Remove; schema ships as reviewed up/down scripts. Also: the app login then needs DDL rights. (scan, all; `efcore-patterns`)
4. **Invariant mode on a SqlClient service** (`Dockerfile:6-7`). SqlClient refuses to connect; `new CultureInfo("ar-SA")` throws; Alpine has no tzdata either. Fix: `RUN apk add --no-cache icu-libs tzdata` (security adds `icu-data-full`), `ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`. (scan, all; `sqlserver-patterns`, `localization`)
5. **Vulnerable transitive package** (`ClinicReminders.csproj:10-11`). SqlClient 6.1.6 brings `System.Security.Cryptography.Pkcs` 9.0.x (CVE-2026-70162, CVE-2026-84008). Fix: direct `PackageReference` to 10.0.13 with the advisory in a comment; NuGetAudit as errors. (scan, security; `zero-vulns`, `msbuild`)
6. **Base address without a trailing slash** (`Program.cs:22`). `messages` resolves to `/api/messages`; the 404 is caught and only logged, so no SMS is ever sent. Fix: `https://sms.example.net/api/v2/`; better, options with validation. (scan, code; `dotnet-core`)
7. **Time-zone lookup with no fallback in a static field** (`ReminderService.cs:13`). On Alpine without tzdata it throws `TypeInitializationException`; POST and the job both fail. Fix: one shared Riyadh zone with a fixed UTC+3 fallback (no DST), used by the SMS text and the trigger. (scan, code; `localization`)
8. **Any signed-in user schedules for any `PatientId`** (`POST /reminders`, `ReminderService.ScheduleAsync`). IDOR plus SMS to any number. GET treats the caller's `sub` as the patient, so the caller is the patient: take the patient from the caller (or refuse a different one). Validate the input (mobile format and length, appointment in the future). (security, code, dba)
9. **`MapInboundClaims` renames `sub`** (`Program.cs:38`, `HttpCurrentUser`). `FindFirst("sub")!` is null with real tokens; 500s. Fix: `MapInboundClaims = false`, `TryParse`, 401/403 on failure; `ClockSkew` 30 s. (code, security; `auth-patterns`)
10. **Quartz** (`Program.cs:24-36`): persistent store without `ScheduleTriggerRelativeToReplacedTrigger = true` drops a run missed during a deploy (scan, all; `quartz-scheduling`). Also: nothing creates the `QRTZ_` tables; the trigger has no fixed identity; no clustering with more than one replica (code, dba). Either configure the store properly (setting, fixed keys, clustering, Quartz's script applied by the pipeline) or use the in-memory store with a single scheduling replica.

## Warning
11. **Cron trigger with no time zone** (`Program.cs:34`): fires at 09:00 UTC = 12:00 Riyadh. Fix: `.InTimeZone(Riyadh)`. (scan, code, dba; `quartz-scheduling`)
12. **`ar-SA` with its default calendar** (`ReminderService.cs:28`): Hijri on one ICU, Gregorian on another. Fix: a cached ar-SA clone with `GregorianCalendar(Localized)`. (scan, code; `localization`)
13. **`UseSerilog()` replaces the redacting logger factory** (`Program.cs:10-12`): the classified `Mobile` field silently disappears; the redactor never runs. Fix: Serilog as a provider: `builder.Logging.ClearProviders(); builder.Logging.EnableRedaction(); builder.Logging.AddSerilog(new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger(), dispose: true);` Or drop the mobile from the line. (code, security; `pii-masking`)
14. **Paging on `CreatedAt` alone** (`ReminderQueries.PageAsync`): ties at a page boundary are skipped. Fix: order by `CreatedAt, Id`, filter `CreatedAt > c || (CreatedAt == c && Id > id)`, cursor carries both; `TryParse` the cursor, 400 on a bad one; fetch `size + 1` so `next` is null on the last page. Index `(PatientId, CreatedAt, Id)`. (code, dba; `efcore-patterns`)
15. **A mobile over 20 characters** reaches SQL Server; error 2628's "Truncated value" text carries the number into the exception and the logs. Validate before the handler. (security)
16. **Due query has no lower bound** (`DueReminders.TakeAsync`): past appointments go out; permanent failures retry forever. Fix: `now < AppointmentAt < until`, cap attempts. (code, dba)
17. **Deny by default**: no fallback authorization policy. (security)
18. **No ProblemDetails / exception handler / HSTS.** (security)
19. Timestamps are `datetimeoffset(3)`; the rule is `datetime2(3)` UTC. (dba)

## Suggestion
- Unused `MarkSentAsync` and `Reference`; `Created` points to a route that doesn't exist; mapping into `IEntityTypeConfiguration`; `.dockerignore`; rate limit on POST; encrypt the mobile at rest; audit row without a record id.

## Positive
- `TimeProvider` injected; keyset paging with a projection; `[LoggerMessage]` with a classified mobile; bounded `varchar(20)`; sealed classes; typed `HttpClient`; `[DisallowConcurrentExecution]`; the audit row in the same `SaveChanges`.

## Dependency vulnerabilities
- `System.Security.Cryptography.Pkcs` 9.0.x, transitive through `Microsoft.Data.SqlClient` 6.1.6: CVE-2026-70162 (moderate), CVE-2026-84008 (high). Not run here (no NuGet access).
