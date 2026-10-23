## Database Review: MeterSync (pre-production)

Scope: everything under /tmp/claude-0/syncruns/with-proven-1/MeterSync, judged against task.md. This was a read-only review: no files changed, nothing built, no EXPLAIN run (there is no database here). Skills cited: efcore-patterns §9/§11, postgresql-patterns §2/§3/§8/§11, worker-patterns (claims), polly-resilience, localization §6/§10, dotnet-core.

### scan.md rows
1. `Program.cs:13`, singleton `PatientDirectory` holding a scoped `SyncDbContext`: **confirmed, critical.** Every `GET /meters` shares one context. Under concurrent requests that throws "A second operation was started on this context", and once its connection breaks it stays broken until the pod restarts. Fix: `AddScoped<PatientDirectory>()`. `AlertSweeper` as a singleton (`:15`) is fine because it takes `IServiceScopeFactory`.
2. `Program.cs:39`, CORS allows every origin with credentials: **confirmed** (security's area). `Cors:Origins` is in config but never read. Fix: `WithOrigins(config.GetSection("Cors:Origins").Get<string[]>())`.
3. `AlertText.cs:13`, time-zone lookup with no fallback: **confirmed, and worse than the row says.** On an image without ICU, `AlertText.cs:9` `new CultureInfo("ar-SA")` throws in a static initializer, so every SMS fails with `TypeInitializationException`. Where ICU is present, `ar-SA` may format the date in Hijri. Fix: `RiyadhTime` with the fixed UTC+3 fallback, and a Gregorian Arabic culture (localization §6).
4. `Program.cs:30`, `ValidateAudience = false`: **confirmed.** Fix: `ValidAudience = "meter-sync"`.
5. `appsettings.json:6`, base address without a trailing slash: **confirmed.** `.../v2` + `meters/x` resolves to `https://meters.example.net/meters/x`, so every sync gets a 404. Production keeps the value without the slash (task.md), so fix it in code at `Program.cs:19`: add `/` when it's missing.
6. `Program.cs:9`, scope validation off in Production: **confirmed.** Turn on `ValidateScopes` and `ValidateOnBuild` only after row 1 is fixed, or startup fails.

### Critical
1. **Patient safety / data** `MeterSyncService.cs:21-25`: readings of `HI` (above 600) and `LO` (below 20) don't parse as numbers, so they are skipped. The most dangerous readings never raise an alert, and the warning repeats every minute. Fix: store `HI` as High and `LO` as Low, keep `Raw`, and make `ValueMgDl` nullable or hold the bound (600/20).
2. **Patient safety / data** `MeterSyncService.cs:27` with `Reading.cs:50`: `mmol/L` values are stored as mg/dL without conversion. 5.5 mmol/L is saved as 5.5 mg/dL and sends a false "low" SMS; a real high of 15 mmol/L also comes out "low". Fix: multiply by 18 when the unit is `mmol/L` (ordinal, ignore case), and reject any other unit.
3. **Multi-pod duplicate SMS** `AlertSweeper.cs:16-31`: every pod selects the same 100 unsent rows and texts them. The gateway has no idempotency key (VENDOR.md), so each patient gets one SMS per pod. Fix: claim each row atomically before sending. `ExecuteUpdateAsync(r => r.AlertClaimedUntil = now+5m)` `WHERE id=@id AND AlertSentAt IS NULL AND (AlertClaimedUntil IS NULL OR AlertClaimedUntil < now)`; send only when it returns 1, then set `AlertSentAt`. A crashed pod's lease expires and the row is retried, which narrows delivery to at-least-once. The alternative is `FOR UPDATE SKIP LOCKED` (worker-patterns). Don't use one long `pg_try_advisory_xact_lock` transaction: a crash mid-batch would resend every SMS in it.
4. **Head-of-line blocking** `AlertSweeper.cs:28`: one SMS that fails for good (a 4xx, a bad phone number, a missing patient so `phones[...]` throws `KeyNotFoundException`) throws out of the loop. It is first in `OrderBy(Id)`, so it blocks every alert behind it, every minute. Fix: try/catch per row, an attempt count, and a terminal status after N attempts.
5. **No alerts when sync fails** `SyncWorker.cs:14-19`: sync and sweep share one `try`. While the vendor is down (or there's a single 404, or row 5 above), the sweep never runs. Fix: give each step its own try, or its own worker.
6. **Two-pod insert race** `MeterSyncService.cs:18-32`: both pods read `known`, both insert the same `VendorId`, and the unique index (`SyncDbContext.cs:43`) rejects the single `SaveChangesAsync`. That pod loses the whole run for every meter. One vendor error does the same (`:17` throws, nothing is saved). Fix: catch per meter, and insert with `INSERT … ON CONFLICT ("VendorId") DO NOTHING` (unnest batch, postgresql-patterns §8). That also removes the `known` query.

### Warnings
1. **Missing index, unbounded read** `MeterSyncService.cs:18`: no index on `readings.MeterId`. `Reading` has no relationship configured (`SyncDbContext.cs:33-45`), so EF creates no foreign key and no index. Each minute is one sequential scan per meter, plus a load of every vendor ID the meter ever had. At 10k meters and about 30M rows that is minutes of I/O per tick. Fix: `HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId)`, the same for `PatientId`, and `Meter`→`Patient`. These also add the missing foreign keys.
2. **History alert storm**: the vendor returns every reading ever uploaded, so linking a meter texts the patient about months-old out-of-range readings. Fix: alert only for `TakenAt >= LinkedAt` (or a recent window), and save older readings with no alert.
3. **SELECT-then-INSERT** `MeterEndpoints.cs:41-48`: two concurrent links of one serial both pass `AnyAsync`. The second gets a 500 from the unique index instead of a 409. Fix: catch `PostgresException { SqlState: "23505" }` and return Conflict.
4. **Authorization (security's area)** `MeterEndpoints.cs:35`: any authenticated caller can link a meter to any national ID. The meter's SMS (glucose data) then goes to that patient's phone, and 404 vs 409 tells the caller whether a national ID exists. Decide who may link: the patient themself (`patient.Id == sub`) or a clinic policy.
5. **Pool sizing** `Program.cs:11` / `appsettings.json:3`: the default `Maximum Pool Size` is 100 per pod, so several pods can exceed PostgreSQL's 100 connections. Set about 20, agreed with the DBA. With no user-opened transactions, `EnableRetryOnFailure` is safe to add (postgresql-patterns §3).
6. **Patient data in logs** `MeterSyncService.cs:23`: the glucose value is logged as a warning. Log the vendor ID only.
7. **No schema scripts**: the project has no migrations. Production needs a reviewed up/down script pair (efcore-rules). The app correctly never calls `Migrate`/`EnsureCreated`.
8. **Minor**: `MeterEndpoints.cs:35` loads a tracked patient only to read its id; use `.Select(p => p.Id)`. Npgsql's default key is identity BY DEFAULT; house rule is `UseIdentityAlwaysColumns()`. Column names are PascalCase, so they need quotes in hand-written SQL.

Checked and fine: every string is bounded (`HasMaxLength`); `timestamptz`; `numeric(6,1)`; UTC `DateTimeOffset` writes; `GET /meters` filters by the caller's patient ID; the SMS POST isn't retried.

### Index Recommendations
- `CREATE INDEX CONCURRENTLY ix_readings_meter_id ON readings ("MeterId")`: used at `MeterSyncService.cs:18`, and by the foreign key.
- `CREATE INDEX CONCURRENTLY ix_readings_patient_id ON readings ("PatientId")`: for the foreign key.
- Replace `(Alert, AlertSentAt)` at `SyncDbContext.cs:44` with a partial index: `CREATE INDEX CONCURRENTLY ix_readings_alert_due ON readings ("Id") WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL` (`HasFilter`). It stays tiny, and `<>` can't use the current index well.

### Migration Safety
The first deployment is a new schema, so there are no N-1 concerns. Before it ships, add the foreign keys, the indexes above and the claim column to the model. Every later index on `readings` needs `IsCreatedConcurrently()` and must be scripted without `--idempotent`.

Not checked: CORS, auth and HTTP details beyond the scan rows (security and code reviewers); query plans (no database); whether the claim design passes the hidden two-instance tests.