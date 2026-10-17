# /full-review: ClinicReminders (round 2; consolidated from the scan, code-reviewer, security-reviewer, dba-reviewer)

## Critical
1. **Singleton `DueReminders` holds a scoped `RemindersDbContext`** (`Program.cs:20`, `DueReminders.cs`, `SendDueRemindersJob.cs:16`). The root context tracks the loaded reminders; `MarkSent` changes them; the job saves its own context, which tracks nothing, so `SentAt` is never written and every unsent reminder is re-sent every run. Fix: delete it (or make it scoped), load and save in one scoped context; `ValidateScopes` + `ValidateOnBuild` in every environment. (scan, all three; `dotnet-core`)
2. **Send-then-save, no idempotency key** (`SendDueRemindersJob.Execute`, `ReminderService.SendAsync`). One `SaveChanges` after the loop; a timeout (`TaskCanceledException`, not caught), a `TypeInitializationException`, shutdown or a pod kill skips it and everything already sent goes out again. Choose at-most-once with a durable claim: claim each row with a conditional `ExecuteUpdateAsync` (`WHERE SentAt IS NULL AND ClaimedAt IS NULL`) before sending, proceed only if one row changed; send; record `SentAt` right away. On a provider error status release the claim; on no response (timeout) leave it claimed and log only the reminder id. Catch per row. (code, security, dba; `outbox`)
3. **Base address without a trailing slash** (`Program.cs:22`): every SMS goes to `/api/messages`, gets a 404, is caught and logged; nothing is ever sent. Fix: `SmsOptions` with `[Required] Uri BaseAddress`, a trailing-`/` validation, `ValidateOnStart()`. Keep the POST off any retry (polly-resilience: no retry without an idempotency key). (scan, code; `dotnet-core`)
4. **Quartz** (`Program.cs:24-36`): persistent store without `ScheduleTriggerRelativeToReplacedTrigger = true` drops a run missed during a deploy (scan; `quartz-scheduling`). The trigger has no fixed identity (the setting can't find it; deploys may add triggers), and no clustering (two pods both fire). Either name the job and trigger, set the option, `AwaitApplicationStarted`, cluster, and apply Quartz's script from the pipeline; or use the in-memory store with a single replica. (code, dba)
5. **The app creates the schema** (`Program.cs:43-46`, `EnsureCreatedAsync`): remove; ship reviewed up/down scripts. (scan, all; `efcore-patterns`)
6. **Invariant mode on a SqlClient service** (`Dockerfile:6-7`): SqlClient can't connect; `new CultureInfo("ar-SA")` throws; Alpine has no tzdata. Fix: `RUN apk add --no-cache icu-libs icu-data-full tzdata`, `ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`. (scan, all; `sqlserver-patterns`, `localization`)
7. **Vulnerable transitive package** (`ClinicReminders.csproj`): SqlClient brings `System.Security.Cryptography.Pkcs` 9.0.x (CVE-2026-70162, CVE-2026-84008). Fix: direct `PackageReference` 10.0.13 with the advisories in a comment; NuGetAudit with NU1901-NU1904 as errors; run `dotnet list package --vulnerable --include-transitive` and clear every hit. (scan, security; `zero-vulns`, `msbuild`)
8. **IDOR: `PatientId` and `Mobile` from the body** (`POST /reminders`, `ScheduleAsync`). GET already scopes by the caller: take the patient from the caller. (security, code, dba)
9. **JWT defaults lose `sub`** (`Program.cs:38`, `HttpCurrentUser`): `MapInboundClaims` renames it, `FindFirst("sub")!` is null, every real request is a 500. Fix: `MapInboundClaims = false`, `ClockSkew` 30 s, `TryParse`, 401/403 on failure. (code, security; `auth-patterns`)

## Warning
10. **Cron trigger with no time zone** (`Program.cs:34`): 09:00 UTC is 12:00 Riyadh. Fix: `.InTimeZone(RiyadhTime.Zone)` with the fixed-offset fallback. (scan, all; `quartz-scheduling`)
11. **Time-zone lookup with no fallback in a static field** (`ReminderService.cs:13`): without tzdata `TypeInitializationException` breaks POST and the job. Fix: `RiyadhTime.Zone` with a fixed UTC+3 fallback. (scan, all; `localization`)
12. **`ar-SA` with its default calendar** (`ReminderService.cs:28`): a cached ar-SA clone with `GregorianCalendar(Localized)`. (scan, code; `localization`)
13. **`UseSerilog()` replaces the redacting factory** (`Program.cs:10-12`): the redactor never runs and `{Mobile}` vanishes. Fix: Serilog as a provider, `builder.Logging.ClearProviders().AddSerilog(logger, dispose: true)`, keep `AddRedaction`/`EnableRedaction`; add the masking pattern layer. (code, security; `pii-masking`)
14. **Paging on `CreatedAt` alone** (`ReminderQueries.PageAsync`): ties at a page edge are skipped; a bad cursor is a 500. Fix: `OrderBy(CreatedAt).ThenBy(Id)`, filter `CreatedAt > c || (CreatedAt == c && Id > id)`, cursor carries both, `TryParse` with a 400. Index `(PatientId, CreatedAt, Id)`. (code, security, dba; `efcore-patterns`)
15. **Due window**: no lower bound (past appointments get texted); failures retried forever. Filter `AppointmentAt > now`; cap attempts. (code, security, dba)
16. **Validation**: no validator; a mobile over 20 characters is a 500; past appointments accepted. Validate mobile (ASCII digits, at most 20) and `AppointmentAt > now` from `TimeProvider`. (all)
17. **Deny by default**: fallback authorization policy. **ProblemDetails / exception handler / HSTS / security headers.** (security, code)
18. **Rate limit** the POST that triggers SMS (per caller). (security)
19. Timestamps `datetimeoffset(3)`; the rule is `datetime2(3)` UTC with a converter. (dba)

## Suggestion
- **`ReminderService.cs:37` `LogError(ex, ...)` passes the exception unmasked:** log a `[LoggerMessage]` with the reminder id, exception type and status code, without the exception. (security)
- Audit row without a record id; encrypt the mobile at rest; `.dockerignore`; unused `MarkSentAsync` and `Reference`; `Created` points to a route that doesn't exist; mapping into `IEntityTypeConfiguration`.

## Dependency vulnerabilities
- `System.Security.Cryptography.Pkcs` 9.0.x through `Microsoft.Data.SqlClient` 6.1.6: CVE-2026-70162 (moderate), CVE-2026-84008 (high).
