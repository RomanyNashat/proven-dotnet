## Database Review: HomeReadings (PostgreSQL, EF Core 10). dba-reviewer

I read all 16 files in the service. There are no migrations and no tests in the folder. The Roslyn MCP wasn't used: the folder is small and I read every file directly.

### Known-traps scan: `Program.cs:7` scope validation. **Confirmed, stays info.**
This is in my area because it covers DbContext lifetime. Production skips `ValidateScopes`/`ValidateOnBuild`, and your tests run in Production, so a captive DbContext would never be caught. No captive dependency exists today: the singletons `ReadingNotifier`, `PhotoStore` and `TimeProvider` take only `IServiceScopeFactory`, `IConfiguration`, `TimeProvider` and `ILogger`. Fix as the scan says (`dotnet-core`, `ValidateServicesInEveryEnvironment`).

### Critical
1. **Cache leaks PHI** `Application/ReadingQueries.cs:32`: the key `"readings:latest"` is the same for every patient. For up to a minute, every patient on that pod gets the first caller's latest glucose reading. The cache is also never cleared when a new reading arrives. Fix: key `readings:latest:v1:{patient.Id}` and remove it in `AddAsync`. (`caching`: keys prefixed and per entity, with a deliberate invalidation.)
2. **IDOR** `ReadingQueries.cs:12-16`: `GetAsync` filters on `Id` only. Ids are sequential ints, so any patient can read anyone's readings. Fix: `.Where(r => r.Id == id && r.PatientId == patient.Id)` and return 404. The PK is still used. (`rules/security.md` "every lookup by ID checks ownership"; `efcore-patterns` §3.)
3. **Alert relay sends duplicates and can stop the API** `Application/ReadingNotifier.cs:15-22, 33-36`, `Program.cs:20-21`. The care team has no idempotency key, so every resend reaches a nurse.
   - **One failure stops the whole API.** If the care team is down or returns 4xx for one alert, the exception escapes `ExecuteAsync`. The default `StopHost` behaviour then shuts the whole API down, and on restart it hits the same alert again: a crash loop. That one alert also blocks everything queued after it.
   - **More than one pod sends each alert once per pod.** Nothing claims rows, so every replica picks up the same 50.
   - **`AddStandardResilienceHandler` retries the POST**, so a timed-out alert that actually arrived is sent again.

   Fix:
   - Add `options.Retry.DisableForUnsafeHttpMethods()` (`polly-resilience`).
   - Wrap each run in `try/catch when not OperationCanceledException` (`worker-patterns`, PeriodicSyncWorker).
   - Claim rows with `FOR UPDATE SKIP LOCKED`. Order across patients doesn't matter. Commit the claim (`claimed_until`) before the POST, keep the per-alert `SaveChanges` after it (it narrows the duplicate window), and record `Attempts` and the exception *type* in `LastError` (`worker-patterns` WorkClaims, `outbox` relay).
   - One crash window (sent, then crash before `MarkSent`) can't be closed without a key on the care team's side. Write that down and ask them for an idempotency key.
4. **A high reading can be saved with no alert** `Application/ReadingService.cs:16-23`: two `SaveChanges` calls. If the pod dies or the second insert fails, the reading is committed and the care team is never told. Fix: give `CareTeamAlert` a `Reading` navigation, add both and call one `SaveChangesAsync`, which is one transaction (`efcore-patterns` §7 "first ask whether one save does it"; `outbox` "same transaction").
5. **The national ID comes from the request body** `ReadingService.cs:14`, `Domain/Reading.cs:26`, sent at `ReadingNotifier.cs:19`. A patient (or a bug in the app) can send any 10-char ID, and the nurse's alert is then filed against another person. An 11-char value fails the `varchar(10)` insert (22001) and the reading is lost with a 500. Fix: take it from the authenticated identity or a patients table, and don't copy a national identifier into every reading row (`healthcare-compliance`: identifiers encrypted at rest).
6. **`CollectedAt` with a non-UTC `Kind` makes Npgsql refuse the insert** `ReadingService.cs:7,14`. System.Text.Json gives `Unspecified` for "2026-10-22T08:00:00" and `Local` for "+03:00". Npgsql refuses both for `timestamptz`, so the reading (and its alert) is lost with a 500. This holds unless the app always sends `Z`. Fix: make `NewReading.CollectedAt` a `DateTimeOffset`, require an offset, and store `.UtcDateTime`. Never `SpecifyKind(Utc)` a Riyadh local time, which shifts it by 3 hours (`postgresql-patterns` §2).
7. **Stored glucose value depends on `x-language`** `ReadingService.cs:14` with `Api/LanguageMiddleware.cs:14`. `decimal.Parse` uses `CurrentCulture`, which the middleware sets to `ar-SA`. There "7.8" can fail (the separator is `٫`); under `en-US`, "7,8" parses as **78**, gets stored and fires a false alert. Arabic-Indic digits fail under every culture. Fix: the header sets `CurrentUICulture` only; parse with `InvariantCulture` after mapping `٠-٩` and `٫` to ASCII; add a range check (`localization` §1 and rule 1). This overlaps with code review; it's listed here because it decides what lands in `value`.
8. **Unbounded `Notes` column (hard ban)** `Infrastructure/ReadingsDbContext.cs:13-24` (`Reading.Notes`, `Reading.cs:30`) has no `HasMaxLength`, so it becomes `text`. Fix: `HasMaxLength(500)` plus request validation (`rules/efcore-rules.md`, `efcore-patterns` §2).

### Warnings
1. **"Today" is the UTC day** `ReadingQueries.cs:21-22`: between 00:00 and 03:00 Riyadh the chart shows yesterday and misses tonight's readings. Fix: build the day from Riyadh midnight using `RiyadhTime` (it falls back when tzdata is missing) and convert it to a UTC range (`efcore-patterns` §9, `localization` §7). The existing `(PatientId, CollectedAt)` index still serves it.
2. **No database integrity on alerts** `ReadingsDbContext.cs:25-32`: no FK `ReadingId → readings`, no unique index on `ReadingId`, no `CHECK` on value or unit. Fix: navigation + FK, a unique index on `ReadingId`, and `CHECK ("Value" > 0 AND "Value" < 100)`.
3. **`Unit` is free text but the threshold assumes mmol/L** `Reading.cs:7,34`, `ReadingsDbContext.cs:19`. A mg/dL reading (for example 120) always alerts, and 11.1 is meaningless for it. Fix: an allowlist and convert to mmol/L when writing.
4. **POST /readings has no idempotency key** `Api/ReadingEndpoints.cs:11-15`: when the app retries on a bad network, you get duplicate readings and duplicate nurse alerts. Fix: a client-generated key (a native `uuid` is allowed, since the ID comes from outside), with a unique index on `(PatientId, ClientKey)` and `ON CONFLICT DO NOTHING` (`postgresql-patterns` §8, `efcore-patterns` §7).
5. **The DB check runs on liveness** `Program.cs:31,40`: a database blip fails `/health/live` and Kubernetes restarts every pod at once. Fix: tag the check `ready`; live uses `Predicate = _ => false` (`worker-patterns`).
6. **Pool sizing** `appsettings.json:3`, `Program.cs:11`: no `Maximum Pool Size`, `Timeout` or `Command Timeout`. With Npgsql's default of 100 per pod, two pods already exceed PostgreSQL's default `max_connections` of 100. Fix: set them and agree them with the DBA. There's also a credential in a committed file (`postgresql-patterns` §3).
7. **No migration scripts** exist, so the schema can't be reviewed. Generate the up/down pair (`rules/efcore-rules.md`).
8. **Photo key comes from the client's filename** `Infrastructure/PhotoStore.cs:12`, `ReadingService.cs:37-38`. A name over 260 chars fails the save after the file is written, leaving an orphaned PHI file. The volume may also be local to one pod. Fix: a server-generated bounded key in object storage (`rules/efcore-rules.md` "no binary columns"). The path traversal here is for the security reviewer.

### Suggestions
- `ReadingNotifier.cs:18`: one query per alert (N+1, 51 round trips for 50 alerts). Do it in one join or projection, which is negligible at this volume, but keep the per-alert commit.
- `ReadingQueries.cs:37`: add `.ThenByDescending(r => r.Id)` so a tie on `CollectedAt` gives a stable result.
- Table names are snake_case but columns are quoted PascalCase (`"SentAt"`), so hand-written SQL needs quotes. Pick one convention (`postgresql-patterns` §2).
- For the other reviewers: `ErrorMiddleware.cs:16` returns raw `PostgresException` text (table and constraint names) to clients; `ReadingService.cs:25` `{@Reading}` logs the national ID and the glucose value.

### Index Recommendations
- `CREATE UNIQUE INDEX ix_care_team_alerts_reading_id ON care_team_alerts ("ReadingId");` for the FK and one alert per reading (`ReadingNotifier.cs:18`).
- `CREATE INDEX ix_care_team_alerts_pending ON care_team_alerts ("Id") WHERE "SentAt" IS NULL;` replaces `HasIndex(SentAt)` (`ReadingsDbContext.cs:31`) for `ReadingNotifier.cs:15`. It holds only pending rows, so it stays a few KB however many alerts have been sent.
- `readings ("PatientId", "CollectedAt")` already serves `TodayAsync` and `LatestAsync`.
- If warning 4 is adopted: `CREATE UNIQUE INDEX ix_readings_patient_client_key ON readings ("PatientId", "ClientKey");`

### Migration Safety
I can't assess it because no migrations exist. Both tables are new, so in the first script a plain `CREATE INDEX` is fine. Any later index on `readings` needs `IsCreatedConcurrently()`, scripted without `--idempotent`, with `SET lock_timeout` at the top and FKs added `NOT VALID` then validated (`postgresql-patterns` §5).

**Not checked:** query plans (`EXPLAIN`), how many replicas run, whether the app sends `Z` timestamps, and the in-process tests (not in the folder).