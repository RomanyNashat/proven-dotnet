# MeterSync: consolidated review

Sources: C = code review, S = security review, D = DBA review, K = known-traps scan. All six scan rows were confirmed by every reviewer who looked at them.

**Verdict: not ready for production.** 29 findings: 9 critical, 5 high, 8 medium, 7 low. Every fix below keeps what task.md fixes in place: `Program`, the routes and their request and response shapes, `SyncDbContext.Patients` and `new Patient(nationalId, phone)`, `SyncOnceAsync`/`RunOnceAsync`, the typed `HttpClient`s, `TimeProvider`, JWT bearer with audience `meter-sync` and `sub` as the patient id, and production's settings values.

## Critical

**1. Any caller can link a meter to any patient (IDOR)**: `Api/MeterEndpoints.cs:35-46`. Raised by C, S, D.
`POST /meters` links the meter to whoever owns `request.NationalId` and never checks the caller. Foreign readings and SMS alerts land in someone else's record, and the 404/409/201 answers reveal which national IDs exist.
Fix: keep the body. Load the caller's patient from `CurrentPatient.Id(user)` and return `NotFound()` when it's missing or its `NationalId` differs from the request's. If clinic staff are meant to call this instead, require a staff policy; ask which caller is intended.
Skill: `auth-patterns` §4 (someone else's record is a 404), `api-design` §11, `rules/security.md` ownership rule.
Settled: D filed it as a warning (outside its area); critical, because it exposes and corrupts health records.

**2. Singleton `PatientDirectory` holds a DbContext**: `Program.cs:13`. Raised by K, C, S, D.
One context for the process's life, never disposed. Two concurrent `GET /meters` throw "A second operation was started on this context" and return 500. Production doesn't validate scopes, so it starts anyway.
Fix: `AddScoped<PatientDirectory>()`.
Skill: `dotnet-core` (the host, captive dependency).

**3. Vendor calls drop `/v2` and never sync**: `appsettings.json:6`, fix at `Program.cs:19` and `:22`. Raised by K, C, S, D.
`https://meters.example.net/v2` has no trailing slash, so `meters/{sn}/readings` resolves to `/meters/...`, the vendor answers 404, `GetFromJsonAsync` throws, and no reading ever arrives.
Fix: append `/` to the base address at registration when it's missing (both clients). Don't change the setting and don't add a start-up check that rejects it.
Skill: `dotnet-core` ("HTTP clients").
Settled: K rated it a warning; critical, because no reading syncs at all. Normalize rather than validate (S offered either): task.md fixes the production value, so a rejecting check would block the deploy.

**4. `HI` and `LO` readings are dropped**: `Application/MeterSyncService.cs:21-25`. Raised by C, S, D.
They fail `TryParse` and are skipped, so the most dangerous readings (over 600, under 20) never alert. They come back every minute and log the same warning each time.
Fix: map `HI` to a High alert and `LO` to a Low alert, store 600 or 20, keep `Raw`.
Skill: none cited (VENDOR.md).

**5. mmol/L readings are stored as mg/dL**: `Application/MeterSyncService.cs:21-27`, `Domain/Reading.cs:41-50`. Raised by C, S, D.
15 mmol/L (270 mg/dL) is stored as 15 and the patient is told their sugar is low; a normal 5.5 mmol/L gets a false Low.
Fix: `Reading.FromMeter` takes the raw value and unit, multiplies mmol/L by 18 into `ValueMgDl`, keeps `Raw`/`Unit` as shown, and rejects unknown units.
Skill: none cited (VENDOR.md:14).

**6. Every pod sends the same SMS**: `Application/AlertSweeper.cs:16-30`. Raised by C, S, D.
All pods select the same unsent rows and the gateway has no idempotency key (VENDOR.md:19), so each patient gets one SMS per pod. A send followed by a failed `SaveChanges` also resends.
Fix: add a lease column (`AlertClaimedUntil timestamptz`). Claim with `UPDATE ... WHERE "Id" IN (SELECT ... AND ("AlertClaimedUntil" IS NULL OR "AlertClaimedUntil" < @now) ORDER BY "Id" LIMIT 100 FOR UPDATE SKIP LOCKED) RETURNING "Id"`, commit the claim, then send and mark each row. State the trade-off: a pod that dies between sending and marking causes one resend after the lease expires.
Skill: `worker-patterns` ("Claiming a batch"), `outbox`.
Settled: C also offered an advisory lock so one sweeper runs; use the lease claim (D), because a lock held over the sends rolls back every mark on a crash and resends the whole batch.

**7. One bad row stops all alerts for good**: `Application/AlertSweeper.cs:28`. Raised by C, S, D.
`phones[reading.PatientId]` throws `KeyNotFoundException` for a reading with no patient (no FK, see 16); a gateway 4xx or the `AlertText` failure (10) does the same. The row stays first in `ORDER BY Id`, so every later run fails at it and no patient is texted again.
Fix: try/catch per reading, add `AlertAttempts`/`AlertFailedAt` and move a row aside after N failures.
Skill: none cited.
Settled: S rated it high; critical, because it silently ends all alerting.

**8. One bad meter stops sync and the sweeper**: `Application/MeterSyncService.cs:15-32`, `SyncWorker.cs:14-19`. Raised by C, D, S.
A vendor 404 for a mistyped serial, or a value too long for `Raw` (16) / `VendorId` (64) or over `numeric(6,1)` (22001/22003), fails the single `SaveChangesAsync` and loses the whole run, every minute. The sweeper is in the same `try`, so it is skipped too.
Fix: fetch and save per meter inside its own try/catch; validate lengths and range before insert; give the sync and the sweeper separate `try` blocks.
Skill: none cited.

**9. Two pods insert the same reading and throw away a run**: `Application/MeterSyncService.cs:18-27`, unique index at `Infrastructure/SyncDbContext.cs:43`. Raised by C, D.
SELECT-then-INSERT: both pods insert the same `VendorId`, one hits 23505, and its batch rolls back.
Fix: `INSERT ... ON CONFLICT ("VendorId") DO NOTHING` via `ExecuteSqlAsync` with interpolated parameters (one statement per meter with `unnest`), or catch 23505. Optional: `pg_try_advisory_xact_lock` so one pod calls the vendor per minute.
Skill: `postgresql-patterns` §8, §11.

## High

**10. Alert text: Hijri dates, and no culture or zone on a slim image**: `Application/AlertText.cs:9`, `:13`, `:14`. Raised by K, C, S, D.
`FindSystemTimeZoneById` throws `TimeZoneNotFoundException` without tzdata. Plain `ar-SA` prints a Hijri date on some ICU builds and Gregorian on others; without ICU the static `new CultureInfo("ar-SA")` throws, so every `AlertText.For` fails with `TypeInitializationException`.
Fix: a static `RiyadhTime.Zone` with a fixed UTC+3 fallback; format with an Arabic culture whose calendar is set to Gregorian, or `yyyy-MM-dd HH:mm` with `InvariantCulture` where the image has no ICU. Add tzdata (and ICU if wanted) in the image.
Skill: `localization` §1, §6, §7.
Settled: K rated it a warning; high, because on an image without ICU every alert fails, and on some ICU builds patients get the wrong calendar.

**11. Audience not validated**: `Program.cs:30`. Raised by K, C, S, D.
A token issued to any other client in the realm is accepted, and `sub` becomes the patient id directly.
Fix: `o.Audience = "meter-sync"`, `ValidateAudience = true`.
Skill: `auth-patterns` §1 ("Audience").
Settled: K warning, S high, C critical; high, because the token still names the same patient, so the risk is a token leaked from another client being replayed here, not one patient reading another's data.

**12. CORS echoes every origin with credentials**: `Program.cs:39-40`. Raised by K, C, S, D.
Fix: `.WithOrigins(config.GetSection("Cors:Origins").Get<string[]>())`, explicit methods and headers, no `AllowCredentials`.
Skill: `owasp-aspnetcore` §3.
Settled: K and C critical, S high; high, because the API uses bearer tokens and no cookies, so another site can't attach credentials today. It becomes critical the moment cookie auth is added.

**13. Linking a meter texts its whole history**: `Application/AlertSweeper.cs:17`. Raised by C, D.
The vendor returns every reading ever uploaded (VENDOR.md:3), so the first sync sends an SMS for every past out-of-range reading, 100 per minute.
Fix: alert only on readings taken after `LinkedAt` (minus an agreed window); mark older ones as needing no SMS.
Skill: none cited.
Settled: C critical, D warning; high, because the readings are real but old (alarm fatigue and SMS cost, not a missed or false value), and the window is a product decision.

**14. Whoever links a serial first gets that meter's readings**: `Api/MeterEndpoints.cs:35`. Raised by S.
Nothing proves the caller holds the meter.
Fix: not fixable within the fixed request shape. Raise with product or the vendor (a pairing code). Found, not fixed.
Skill: none cited.

## Medium

**15. Linking the same serial twice at once returns 500; serials are case-sensitive**: `Api/MeterEndpoints.cs:41-48`. Raised by C, S, D.
Both requests pass `AnyAsync` and the unique index rejects one with 23505. On PostgreSQL `ab12` and `AB12` are two meters (D).
Fix: catch `PostgresException { SqlState: "23505" }` and return `Conflict()`. Make the uniqueness check case-insensitive.
Skill: `postgresql-patterns` §2.
Settled: D suggested storing `ToUpperInvariant()`; compare case-insensitively but keep the serial as sent, because `GET /meters` returns `serialNumber` and task.md keeps that response.

**16. No foreign keys**: `Infrastructure/SyncDbContext.cs:16-45`. Raised by C, D, S.
No FK from meters or readings to patients, or readings to meters; orphan rows feed finding 7.
Fix: `HasOne<Patient>().WithMany().HasForeignKey(...)` for each (this also creates the `MeterId` index).
Skill: none cited.

**17. Per-meter query of all known IDs, and no index on `readings.MeterId`**: `Application/MeterSyncService.cs:18`, `Infrastructure/SyncDbContext.cs:33-45`. Raised by C, D.
One query per meter per minute loading every vendor ID for that meter, with no index (around 15M rows a year at 10k meters).
Fix: finding 9's `ON CONFLICT` removes the query; otherwise `Where(r => incomingIds.Contains(r.VendorId))` (served by the unique index) and `CREATE INDEX CONCURRENTLY ix_readings_meter_id ON readings ("MeterId");`.
Skill: `postgresql-patterns` §8.

**18. Health data in logs**: `Application/MeterSyncService.cs:23`, default `HttpClient` logging. Raised by C, S, D.
The glucose value is logged with its reading ID; the default HttpClient logs write the vendor URI, which carries the serial number.
Fix: log the `VendorId` and a reason only; `.RemoveAllLoggers()` on both typed clients, or raise `System.Net.Http.HttpClient` to Warning.
Skill: `pii-masking`, `rules/security.md` (Data Protection).

**19. Missing or non-numeric `sub` returns 500**: `Api/CurrentPatient.cs:9`. Raised by C, S.
`int.Parse` with `!`.
Fix: `MapInboundClaims = false`, read `sub` (still the patient id), `TryParse`, return 401 on failure.
Skill: `auth-patterns` §5.

**20. No ProblemDetails, exception handler, security headers or HSTS**: `Program.cs:44-49`. Raised by S, C.
`GET /meters` returns patient-linked data with no `Cache-Control: no-store`.
Fix: `AddProblemDetails` + `UseExceptionHandler`, headers set in `OnStarting`, HSTS for 365 days with ForwardedHeaders behind nginx.
Skill: `owasp-aspnetcore` §1, §2.

**21. Connection pool not sized for several pods**: `appsettings.json:3`. Raised by D.
No `Maximum Pool Size`, so each pod gets Npgsql's 100, and several pods exceed PostgreSQL's default 100 connections.
Fix: agree a size with the DBA (e.g. 20). Production's connection string is fixed (task.md), so set it on an `NpgsqlConnectionStringBuilder` in code when the string doesn't carry one. If `EnableRetryOnFailure` is added, run the claim in finding 6 inside `CreateExecutionStrategy()`.
Skill: `postgresql-patterns` §3.

**22. Settings read with `!` and no validation**: `Program.cs:19`, `:22`, `:27`. Raised by C (S in passing).
A missing setting fails on the first call, not at start-up.
Fix: options with `ValidateOnStart`, but no trailing-slash rule (see 3).
Skill: `dotnet-core` ("Options").

## Low

**23. Scope validation is off in Production**: `Program.cs:9`. Raised by K, C, S, D.
Fix: `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })`. Safe once 2 is fixed: `AlertSweeper` and `SyncWorker` already use `IServiceScopeFactory`.
Skill: `dotnet-core`.
Settled: C named a `ValidateServicesInEveryEnvironment()` helper; use the explicit `UseDefaultServiceProvider` form (S, D), the standard host API.

**24. The sweeper's index doesn't fit its query**: `Infrastructure/SyncDbContext.cs:44`. Raised by C, D.
`Alert <> 'None'` isn't an equality, so `(Alert, AlertSentAt)` serves it poorly.
Fix: `CREATE INDEX CONCURRENTLY ix_readings_alert_due ON readings ("Id") WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL;` (EF: `HasIndex(r => r.Id).HasFilter(...)`), then drop the old one.
Skill: none cited.

**25. Infrastructure depends on the API layer**: `Infrastructure/PatientDirectory.cs:1`, `:12`. Raised by C.
It returns `Api.MeterResponse`.
Fix: return an Application read model, or move the query into Application. The JSON shape stays the same.
Skill: none cited (`rules/architecture.md`).

**26. No per-caller rate limit on `POST /meters`**: `Api/MeterEndpoints.cs`. Raised by S.
Fix: a partitioned limiter keyed on `sub`.
Skill: `api-design` §6.

**27. `NationalId` isn't validated**: `Api/MeterEndpoints.cs:27-35`. Raised by S.
Fix: require it, 10 digits, and return 400 otherwise.
Skill: none cited.

**28. Every alert is a multi-part SMS, and there's no HI/LO wording**: `Application/AlertText.cs:16`. Raised by C.
The Arabic text runs past 70 characters.
Fix: shorten it to one part; add wording for HI and LO (with finding 4).
Skill: `localization` §10.

**29. No migration scripts**: whole project. Raised by D.
Production needs an initial migration as a reviewed up/down script; the tests create the schema from the model, which stays. Use `UseIdentityAlwaysColumns()` (Npgsql defaults to BY DEFAULT). Add the claim/attempt columns as nullable, with `CONCURRENTLY` indexes.
Skill: `efcore-patterns`, `postgresql-patterns`.

## Checked and clean
- No `Migrate()` or `EnsureCreated()`; every string is bounded; keys are `int`; timestamps are `timestamptz`; the value is `numeric(6,1)` (C, D).
- `TimeProvider` is used throughout, including `PeriodicTimer`; the worker's catch skips `OperationCanceledException` (C).
- `AlertSweeper` and `SyncWorker` use `IServiceScopeFactory`; `SaveChanges` per row in the sweeper is deliberate (C, D).
- `SmsClient` has no retry handler, correctly, since its POST isn't idempotent; the vendor GET has the standard resilience handler (C, `polly-resilience`).
- No secrets in `appsettings.json` (S). No repo text addressed to an AI reviewer (C, S).

## Not checked
- `dotnet list package --vulnerable --include-transitive` was not run; packages are pinned at `10.0.0` (`MeterSync.csproj:9-11`). Under `zero-vulns` any hit blocks the release (S).
- Nothing was built or run; there are no tests in the folder. No query plans (no database) (C, S, D).
- nginx and forwarded-header config: none in the folder (C).
- Roslyn MCP was not used; all files were read directly (C, D).
