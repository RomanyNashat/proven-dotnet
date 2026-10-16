---
name: known-traps
description: Scan a .NET service, or many, for the traps this harness's own tests found in real libraries — a Quartz store that drops missed runs, SqlClient in invariant mode, a singleton holding a DbContext, ar-SA's machine-dependent calendar, OpenIddict 6 on EF Core 10 and more. One read-only command, one table with file, line, fix and the skill that proves it.
version: 1.1.0
---

# Known traps: one scan for what the tests found

The skills' tests found bugs in how real libraries behave: Quartz drops a run that was due while the
service was down, SqlClient refuses to connect without ICU, `ar-SA` changes calendar with the machine.
Each one is fixed in its skill. This scan looks for them in a service you already have, so a team can
check every service once instead of reading each by hand.

## Run it

```bash
python ~/.claude/skills/known-traps/scripts/scan_traps.py path/to/ServiceA path/to/ServiceB
```

On Windows: `py -3 "$HOME\.claude\skills\known-traps\scripts\scan_traps.py" C:\src\ServiceA C:\src\ServiceB`.

- Read-only, standard library only, no build or restore needed.
- One table per service: severity, `file:line`, what, the fix, and the skill whose test proves it.
- `--json` for a machine-readable result; `--fail-on critical` (or `warning`) sets exit code 1, so a
  pipeline can use it as a gate.
- `bin`, `obj` and `.git` are skipped. Commented-out lines aren't findings. Test projects are only
  checked where it matters (the FluentAssertions licence, a test fixture that migrates).

**A finding is a place to read, not a proof.** It's a text scan: it points at the line, and the skill
explains the bug and shows the tested fix. A clean result means none of these patterns was found, not
that the service is right. For "who calls this" and "is this still used", use `proven-roslyn`.

## The checks

| Check | Severity | What it finds | Proven in |
|---|---|---|---|
| `app-changes-schema` | critical (warning in tests) | `Database.Migrate()`, `MigrateAsync()`, `EnsureCreated()` | `efcore-patterns` |
| `sqlclient-invariant-mode` | critical | invariant globalization on a service that uses SqlClient: it can't connect | `sqlserver-patterns` |
| `sqlclient-pkcs-unpinned` | warning | SqlClient without a direct `System.Security.Cryptography.Pkcs` 10.0.13+ | `msbuild` |
| `merge-without-holdlock` | warning | a `MERGE` that races without `HOLDLOCK` | `dapper-patterns` |
| `online-index-op` | info | `ONLINE = ON`: Enterprise only | `sqlserver-patterns` |
| `arabic-text-not-unicode` | warning | an Arabic (`…Ar`, `…_ar`) column mapped to `varchar` | `localization` |
| `singleton-holds-dbcontext` | critical | a singleton whose constructor takes a `DbContext` | `dotnet-core` |
| `scopes-not-validated` | info | no `ValidateScopes` for every environment | `dotnet-core` |
| `options-not-validated-on-start` | info | `AddOptions<T>()` without `ValidateOnStart()` | `dotnet-core` |
| `base-address-loses-path` | warning | a base address with a path and no trailing `/` (code or appsettings) | `dotnet-core` |
| `ar-sa-default-calendar` | warning | `ar-SA` used without setting its calendar | `localization` |
| `timezone-without-fallback` | warning | `FindSystemTimeZoneById` with no fallback for an image without tzdata | `localization` |
| `windows-timezone-id` | warning | a Windows time-zone id (`"Arab Standard Time"`): it needs ICU on Linux | `localization` |
| `quartz-missed-run-dropped` | critical | a persistent store without `ScheduleTriggerRelativeToReplacedTrigger = true` | `quartz-scheduling` |
| `quartz-cron-in-utc` | warning | a cron trigger without `InTimeZone` | `quartz-scheduling` |
| `quartz-script-drops-tables` | critical | Quartz's schema script with `DropDb` still 1 | `quartz-scheduling` |
| `quartz-di-job-factory` | info | `UseMicrosoftDependencyInjectionJobFactory()`, the default since 3.3 | `quartz-scheduling` |
| `hangfire-filter-on-class` | warning | `[AutomaticRetry]`/`[Queue]` on a class that implements an interface | `hangfire-patterns` |
| `hangfire-schema-at-boot` | warning | Hangfire storage without `PrepareSchemaIfNecessary = false` | `hangfire-patterns` |
| `hangfire-dashboard-default-filter` | warning | a dashboard still on the local-requests-only filter | `hangfire-patterns` |
| `rabbitmq-quorum-without-dead-letter` | warning | a quorum queue with no dead-letter exchange | `rabbitmq-patterns` |
| `cap-publish-confirms-off` | warning | CAP on RabbitMQ without `PublishConfirms = true` | `cap-library` |
| `cap-default-retries` | info | CAP with its default 50 retries | `cap-library` |
| `grpc-http-resilience` | warning | `AddStandardResilienceHandler()` on a gRPC client | `grpc-patterns` |
| `signalr-client-picks-group` | critical | a hub method that joins the group named by the client | `signalr` |
| `cors-any-origin` | critical | `SetIsOriginAllowed(_ => true)` | `owasp-aspnetcore` |
| `jwt-audience-not-validated` | warning | `ValidateAudience = false` | `auth-patterns` |
| `keycloak-roles-unmapped` | warning | `RoleClaimType` pointed at Keycloak's JSON role claim | `auth-patterns` |
| `openiddict6-on-efcore10` | critical | OpenIddict 6 with EF Core 10 and no `DisableBulkOperations()` | `openiddict-server` |
| `fluentassertions-licence` | critical (warning if unpinned 7.x) | FluentAssertions 8+, or 7 without `[7.0.0,8.0.0)` | `testing-tdd` |

Tested in CI (`tests/traps/`): every check fires on a service that has its trap and stays quiet on the
corrected copy. Also tested: a pin moved into `Directory.Packages.props` still counts, and a file saved
with a byte-order mark is still read.

## Using the result

1. Run it over every service you own. The table is the work list.
2. For each finding, open the skill in the last column: it explains the bug and shows the tested fix.
3. Fix in the service's own branch, with the usual review. The scan never edits.

A new trap a test finds goes into the scan in the same change: a check, its line in the bad fixture, the
corrected line in the good one, and its row above.

## Rules
- Read-only; it reports, the developer fixes.
- A finding is a place to read; the skill is the proof.
- Every check is backed by a test in a skill. A pattern nobody has tested doesn't go in.
