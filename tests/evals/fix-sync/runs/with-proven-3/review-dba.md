## Database Review: MeterSync (pre-production)

I read every file in `/tmp/claude-0/syncruns/with-proven-3/MeterSync` plus `../task.md` and `../scan.md`. This was a read-only review: I edited nothing and ran nothing. No file contains text aimed at an AI reviewer.

### scan.md, row by row
- `Program.cs:13`, singleton `PatientDirectory` holding `SyncDbContext`: **confirmed, critical.** Every `GET /meters` from every user shares one context. Two requests at the same moment throw "A second operation was started on this context", and its connection and change tracker live as long as the app. Fix: `AddScoped<PatientDirectory>()`. `AlertSweeper` as a singleton (`:15`) is fine because it only takes `IServiceScopeFactory` and `TimeProvider`.
- `Program.cs:39`, CORS allows every origin: **confirmed.** Use `WithOrigins(config.GetSection("Cors:Origins").Get<string[]>())`. That setting already exists and nothing reads it. This belongs to the security reviewer.
- `AlertText.cs:13`, tzdata: **confirmed.** On a slim image it throws on every alert. Use the `RiyadhTime` fallback to fixed UTC+3 (`localization` §7). The scan **missed** `AlertText.cs:9/14`: plain `ar-SA` formats dates in Hijri or Gregorian depending on the image's ICU, and in invariant mode it throws `CultureNotFoundException` from the static initializer. Use `ArabicGregorian` (`localization` §6).
- `Program.cs:30`, `ValidateAudience = false`: **confirmed.** Set `ValidAudience = "meter-sync"`.
- `appsettings.json:6`, base address with no trailing slash: **confirmed.** Requests go to `https://meters.example.net/meters/...` and lose `/v2`. task.md fixes the setting's value, so the fix goes in code: at `Program.cs:19`, add a `/` to the base address if it is missing.
- `Program.cs:9`, scope validation: **confirmed, info.** `ValidateScopes` and `ValidateOnBuild` in every environment would have caught row 1 at startup.

### Critical (data loss / patient safety)
1. **Dropped readings** `MeterSyncService.cs:21-25`: `HI` (>600 mg/dL) and `LO` (<20 mg/dL) fail `decimal.TryParse` and are skipped. The most dangerous readings are never stored and never alerted, and the warning repeats every minute forever. **Fix:** map `HI` to High and `LO` to Low, keep `Raw`, and store the bound (600/20) or a nullable `ValueMgDl` (VENDOR.md:12-13).
2. **Wrong unit** `MeterSyncService.cs:27` / `Reading.cs:50`: `mmol/L` values are never converted. 5.5 mmol/L (normal) is stored as 5.5 mg/dL and alerted as Low. 20 mmol/L (360 mg/dL, high) is also alerted as Low. **Fix:** multiply by 18 for `mmol/L` (VENDOR.md:14) and reject unknown units.
3. **Duplicate insert across pods** `MeterSyncService.cs:18-32`: two pods read the same "known" set and both insert the same `VendorId`. The unique index (`SyncDbContext.cs:43`) then fails the single `SaveChangesAsync`, which loses the whole run for every meter, minute after minute. **Fix:** `INSERT ... ON CONFLICT ("VendorId") DO NOTHING` (`postgresql-patterns` §8), or run the sync on one pod under `pg_try_advisory_xact_lock` (§11). Save per meter so one failure doesn't throw away the others' readings.
4. **Duplicate SMS across pods** `AlertSweeper.cs:16-30`: every pod selects the same due rows and texts them. The gateway has no idempotency key (VENDOR.md:19-20), so each pod sends again. **Fix:** claim rows with `FOR UPDATE SKIP LOCKED` plus a `claimed_by`/`claimed_at` lease (`worker-patterns`, WorkClaims), or one sweeper under an advisory lock. Mark before or after sending is a deliberate trade-off (at-most-once vs at-least-once), and the code should say which it chose.
5. **One failure blocks every alert** `AlertSweeper.cs:26-31`: an SMS failure, or `phones[...]` throwing `KeyNotFoundException` (there is no FK, see Warnings), aborts the loop. Because the query orders by id, the same row comes back first every minute and every alert behind it stays unsent. **Fix:** try/catch per reading, record attempts and the last error, skip after N attempts.

### Warnings
1. **Old readings alert** `MeterSyncService.cs:17-27`: the vendor returns *every* reading. A newly linked meter turns its whole history into SMS alerts, 100 a minute. Alert only on `TakenAt >= meter.LinkedAt` (or a recent window).
2. **N+1 and a growing scan** `MeterSyncService.cs:18`: one query per meter pulls every known `VendorId` for that meter, and `readings` has no index on `MeterId`, so each one is a sequential scan, every minute. At 1k meters with 1M readings, that is about 1k seq scans per tick. **Fix:** query `Where(r => vendorIds.Contains(r.VendorId))` against the existing unique index, or use ON CONFLICT (Critical 3) and drop the read.
3. **No foreign keys** `SyncDbContext.cs:24-45`: there are no relationships, so the database has no FK on `meters.PatientId`, `readings.MeterId` or `readings.PatientId`. Orphan rows are possible, which is what makes `AlertSweeper.cs:28` throw. Add `HasOne<Patient>().WithMany().HasForeignKey(...)` (EF indexes FK columns).
4. **Race on link** `MeterEndpoints.cs:41-48`: the check-then-insert lets two posts at once pass the check. The second hits 23505 and returns 500 instead of 409. Catch `PostgresException { SqlState: "23505" }` and return `Conflict`.
5. **Case-sensitive serial numbers** `MeterEndpoints.cs:41`: PostgreSQL is case-sensitive, so `abc1` and `ABC1` count as two meters. Normalize the serial number before saving and comparing (`postgresql-patterns` §2).
6. **No ownership check** `MeterEndpoints.cs:35`: any signed-in patient can link a meter to any national ID, which sends that meter's readings and SMS to someone else (`rules/security.md`). Check `sub` against the patient, or require a clinic policy. This also belongs to the security reviewer.
7. **Health data in logs** `MeterSyncService.cs:23`: a glucose value is logged with its reading ID. Log the vendor ID only.
8. **Pool and retries** `Program.cs:11`: no `Maximum Pool Size`, so several pods × Npgsql's default of 100 exceeds PostgreSQL's 100 `max_connections`. There is also no `EnableRetryOnFailure` (`postgresql-patterns` §3). Set a pool size agreed with the DBA.
9. **Sync failure stops alerts** `SyncWorker.cs:14-19`: when the vendor is down, the shared try block skips the sweeper too. Give each its own try block.
10. **No timeout on SMS** `Program.cs:21-22`: the SMS client has no timeout policy. Add a timeout, not retries: a POST must not be retried (`polly-resilience`).

### Index recommendations
- `CREATE INDEX CONCURRENTLY ix_readings_alert_due ON readings ("Id") WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL;` replaces `(Alert, AlertSentAt)` at `SyncDbContext.cs:44` for the sweeper query at `AlertSweeper.cs:17-19`. In EF: `HasIndex(r => r.Id).HasFilter(...)`.
- `CREATE INDEX CONCURRENTLY ix_readings_meter_id ON readings ("MeterId");` and `ix_readings_patient_id ON readings ("PatientId");`. These come for free with the FKs.
- Info: table names are snake_case but column names are quoted PascalCase. Pick one convention (§2). Keys use Npgsql's default `BY DEFAULT` identity; the house rule is `UseIdentityAlwaysColumns()`.

### Migration safety
There are no migrations, and the schema comes from the model (tests create it from the model). There is no `Migrate()` in the app, which is good. Making `ValueMgDl` nullable, adding claim columns, FKs and indexes is all additive. Ship them as reviewed scripts: concurrent indexes scripted on their own without `--idempotent`, `SET lock_timeout` first, and FKs added `NOT VALID` and then validated (`postgresql-patterns` §5). These changes are safe for zero downtime with old pods still running (N-1).

### Not checked
I didn't build anything or run any SQL or `EXPLAIN`, and the lock-time estimates are untested. JWT, CORS and SMS text encoding were left to the security and code reviewers. Skills cited are in `/home/claude/proven-dotnet/.claude/skills/`: `efcore-patterns`, `postgresql-patterns`, `worker-patterns`, `localization`, `polly-resilience`.