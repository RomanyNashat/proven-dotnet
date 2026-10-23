## Database Review: MeterSync (/tmp/claude-0/syncruns/with-proven-2/MeterSync)

I read every source file, task.md and scan.md. Skills cited: `efcore-patterns` §11, `postgresql-patterns` §2, §3, §8 and §11, `worker-patterns` (claiming a batch), `outbox` (one runner, side effects), `dotnet-core` (captive dependency). Nothing was run. Roslyn MCP was not used: every file was small enough to read.

### Scan rows
- **Program.cs:13 singleton taking a DbContext: CONFIRMED, critical.** `PatientDirectory(SyncDbContext)` is a singleton, so it keeps one context for the life of the process. Production doesn't validate scopes, so the app still starts. Under load, two concurrent `GET /meters` calls throw "A second operation was started on this context", and the context never releases its connection. **Fix:** `AddScoped<PatientDirectory>()`.
- **Program.cs:9 scope validation: CONFIRMED.** Add `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })` so the bug above fails at startup in every environment.
- Outside my area, but I agree with all four: CORS :39 (use `WithOrigins` from `Cors:Origins`), `ValidateAudience` :30 (set `meter-sync`), AlertText.cs:13 (no tzdata; also `ar-SA` can format the date in Hijri, see `localization`), appsettings.json:6 (`/v2` without a trailing slash makes requests go to `/meters/...`). The production setting can't change, so the fix belongs in code: add `/` when it's missing.

### Critical
1. **[Multi-pod duplicate SMS]** `Application/AlertSweeper.cs:16-30`. Every pod selects the same 100 unsent rows and texts each patient once per pod. The gateway has no idempotency key (VENDOR.md:19). **Fix** (`worker-patterns` claim): add a `AlertClaimedUntil timestamptz` column. Claim rows with `UPDATE readings SET "AlertClaimedUntil"=@leaseEnd WHERE "Id" IN (SELECT "Id" … AND ("AlertClaimedUntil" IS NULL OR "AlertClaimedUntil"<@now) ORDER BY "Id" LIMIT 100 FOR UPDATE SKIP LOCKED) RETURNING "Id"`, then send and mark each row. The claim commits before any send, so a duplicate can only happen if a pod dies between sending and marking. That trade-off has to be stated in the fix. Don't hold one long transaction over the sends instead (an advisory lock alone): a crash then rolls back every mark in the batch and re-sends all of it.
2. **[Poison row stops all alerts]** `AlertSweeper.cs:28`. A patient row that's missing throws `KeyNotFoundException` at `phones[...]`; meters/readings have no FK, see W4. A gateway 4xx, such as a bad phone number, also throws. The row stays first in `ORDER BY Id`, so once the rows ahead of it are sent, every run fails at it and **no patient gets an alert again**. **Fix:** try/catch per row, plus `AlertAttempts`/`AlertFailedAt` columns that move a row aside after N failures.
3. **[One bad meter stops sync and alerts]** `MeterSyncService.cs:15-32` and `SyncWorker.cs:14-19`. One vendor error loses the whole run, because there is a single `SaveChangesAsync` at the end. Typing a serial wrong at `POST /meters` is enough (the vendor answers 404). The sweeper sits in the same `try`, so it is skipped too. **Fix:** try/catch and save per meter, and give the sweeper its own `try`.
4. **[Insert race across pods]** `MeterSyncService.cs:18-27`. This is SELECT-then-INSERT. Two pods insert the same `VendorId`, the unique index at `SyncDbContext.cs:43` throws 23505, and that pod's whole run is thrown away. **Fix** (`postgresql-patterns` §8): `INSERT … ON CONFLICT ("VendorId") DO NOTHING` through `ExecuteSqlAsync` with interpolated parameters, one statement per meter via `unnest`. That also removes the N+1 query of known IDs. Optional: `pg_try_advisory_xact_lock` (§11) so only one pod calls the vendor per minute.
5. **[Wrong or missing glucose values]** `MeterSyncService.cs:21` and `Reading.cs:50`. Readings of `HI` (>600) and `LO` (<20) are skipped, and these are the most dangerous ones. `mmol/L` values are stored as if they were mg/dL: 15 mmol/L (270 mg/dL) gets a *Low* alert, and 3.5 mmol/L (63) also gets Low only by accident. **Fix:** map HI to High and LO to Low. Multiply mmol/L by 18 before storing it in `ValueMgDl`. Keep `Raw`/`Unit` as the meter showed them. Reject unknown units and anything that overflows `numeric(6,1)` (22003) or is over 16 characters for `Raw` (22001), because either error fails the batch.

### Warnings
1. **[Missing index]** `SyncDbContext.cs:33-45`. `readings` has no index on `MeterId`, but :18 filters on it once per meter per minute. With 10k meters at about 4 readings a day, that is around 15M rows a year, scanned 10k times a minute. The fix in C4 removes this query. If it stays, add the index below.
2. **[Alert index can't be used well]** `SyncDbContext.cs:44`. In `(Alert, AlertSentAt)`, the condition `Alert <> 'None'` isn't an equality, so the index is a poor fit. Use a partial index (below).
3. **[Check-then-insert gives a 500]** `Api/MeterEndpoints.cs:41-48`. Two concurrent links of the same serial: the second hits 23505 and returns 500 instead of 409. **Fix:** catch `PostgresException { SqlState: "23505" }` and return Conflict. Serials are also case-sensitive on PostgreSQL (§2): `ab12` and `AB12` can be linked as two meters, and then C4 blocks sync. Store and look up `Trim().ToUpperInvariant()`.
4. **[No FKs]** There are no navigations, so EF creates no FK from meters.PatientId or readings.PatientId/MeterId. Orphan rows are possible, and they feed C2. Add `HasOne<Patient>().WithMany().HasForeignKey(...)`.
5. **[Pool size]** `appsettings.json:3`. There's no `Maximum Pool Size`, so each pod gets Npgsql's 100. Several pods × 100 is more than PostgreSQL's default 100 connections (§3). Agree a size with the DBA (e.g. 20). If `EnableRetryOnFailure` is added, the claim transaction must run inside `CreateExecutionStrategy()`.
6. **[Alert flood when a meter is linked]** The vendor returns every reading the meter has ever uploaded (VENDOR.md:3), so the first sync texts every historical out-of-range reading. Only alert readings taken after `LinkedAt` minus an agreed window (a business decision).
7. **[Health data in logs]** `MeterSyncService.cs:23` logs the glucose value. Log only the VendorId.
8. **[Cross-area IDOR]** `MeterEndpoints.cs:35`. Any authenticated caller can link any serial to any national ID, so a victim's alerts (health data) can be sent to the attacker's own account, and the 404/409 answers reveal which national IDs exist. Check the caller against `sub` or a staff policy.

### Index Recommendations
- `CREATE INDEX CONCURRENTLY ix_readings_meter_id ON readings ("MeterId");` (MeterSyncService.cs:18, if it stays)
- `CREATE INDEX CONCURRENTLY ix_readings_alert_due ON readings ("Id") WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL;` (AlertSweeper.cs:17). In EF: `HasIndex(r => r.Id).HasFilter(...).HasDatabaseName(...)`. Then drop the index at :44.

### Not findings (checked and rejected)
- Every string is bounded.
- Timestamps are `timestamptz` with offset 0.
- `int` keys.
- No `Migrate()`/`EnsureCreated()` in app code.
- `PatientDirectory` projects to plain values, so it needs no `AsNoTracking`.
- `SaveChanges` per row in the sweeper is deliberate: it records each send right after it.

### Migration Safety
The project has no migrations. Production needs an initial migration, scripted as up/down. Use `UseIdentityAlwaysColumns()` for the house rule; Npgsql defaults to BY DEFAULT. New tables carry no lock risk. Adding the claim/attempt columns later must use nullable columns and `CONCURRENTLY` indexes, which is N-1 safe.

Not checked: query plans (no database here) and the hidden tests.