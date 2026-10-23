# MeterSync review (consolidated)

Merged from the code review (C), the security review (S), the DBA review (D) and the known-traps scan (K).
All six scan rows were confirmed by the reviewers. Severity: Critical, High, Medium, Low.
Every fix stays inside what `task.md` says must stay: `Program`, the three routes and their shapes,
`SyncDbContext.Patients` and `new Patient(nationalId, phone)`, `SyncOnceAsync`/`RunOnceAsync`, the typed
clients, `TimeProvider`, JWT on the default scheme with audience `meter-sync`, and the production settings.

**38 findings: 12 Critical, 4 High, 15 Medium, 7 Low.**

## Critical

**1. `HI` and `LO` readings are skipped, so the most dangerous values never alert** (C, D, S)
`Application/MeterSyncService.cs:21-25`, `Domain/Reading.cs:50`. `HI` (over 600 mg/dL) and `LO` (under
20) fail `decimal.TryParse`, get a warning, and are dropped, every minute.
Fix: in the domain, map `HI` to High (store 600) and `LO` to Low (store 20), and keep `Raw`.
Settled: C's bound values, not D's nullable `ValueMgDl` option. The column stays non-null, the alert logic
is unchanged, and `Raw` still puts "HI"/"LO" in the SMS.
Skill: none cited.

**2. mmol/L values are stored as mg/dL** (C, D, S)
`Application/MeterSyncService.cs:27`, `Domain/Reading.cs:50`. 15 mmol/L (270 mg/dL) is saved as 15 and texted
as Low, and 5.5 mmol/L sends a false Low.
Fix: multiply by 18 when the unit is `mmol/L` (ordinal, ignore case). Reject any other unit.
Skill: none cited.

**3. `POST /meters` never checks the caller (IDOR)** (C, S, D)
`Api/MeterEndpoints.cs:24-49` (D: `:35`). Any token can link any serial to any national ID. An attacker can
claim a victim's meter and read their glucose data, or link a meter to a victim and send them false alert
SMS. The 404/409/201 answers also show which national IDs are registered.
Fix: take `ClaimsPrincipal`, load the caller's patient by `CurrentPatient.Id(user)`, and return `NotFound`
unless its `NationalId` equals `request.NationalId`. The body shape stays the same.
Settled: Critical, not D's warning. D asked whether the patient or a clinic may link. The patient may: the
token contract in task.md has `sub` as the patient's id and nothing for a clinic role. A mismatch returns the
same 404 as an unknown ID, so nothing can be enumerated.
Skills: `api-design` §11, `auth-patterns` §4, `rules/security.md`.

**4. CORS reflects every origin, with credentials** (K, C, S, D)
`Program.cs:38-42` (K: `:39`). Any site gets its own origin back plus `Allow-Credentials: true`.
`Cors:Origins` is configured but never read.
Fix: `WithOrigins(config.GetSection("Cors:Origins").Get<string[]>())`, `WithMethods("GET","POST")`,
`WithHeaders("Authorization","Content-Type")`, and no `AllowCredentials`.
Settled: drop credentials (C, S). D only listed the origins. This is a bearer-token API with no cookie
auth, so credentials are never needed.
Skill: `owasp-aspnetcore` §3.

**5. Singleton `PatientDirectory` holds one DbContext for every request** (K, C, S, D)
`Program.cs:13`. Concurrent `GET /meters` calls throw "A second operation was started", return stale data,
and once the connection breaks it stays broken until the pod restarts. S checked: no cross-patient leak,
because the query filters by patient.
Fix: `AddScoped<PatientDirectory>()`. `AlertSweeper` as a singleton is fine; it uses `IServiceScopeFactory`.
Skill: `dotnet-core`.

**6. The vendor base address drops `/v2`, so every sync 404s** (K, C, S, D)
`appsettings.json:6`, fix at `Program.cs:19`. `https://meters.example.net/v2` + `meters/…` resolves to
`/meters/…`.
Fix in code: append `/` to the base address when it's missing. Don't change the setting (task.md fixes it)
and don't refuse it at startup, which would stop production from starting.
Settled: Critical (C), not K's warning. With this bug the service never syncs.
Skill: `dotnet-core`.

**7. Two pods send the same SMS** (C, D, S)
`Application/AlertSweeper.cs:16-31`. Every pod selects the same unsent readings, and the gateway has no
idempotency key (`VENDOR.md:19`).
Fix: claim each row before sending: `ExecuteUpdateAsync` setting `AlertClaimedUntil = now + 5 min` where
`Id = @id AND AlertSentAt IS NULL AND (AlertClaimedUntil IS NULL OR AlertClaimedUntil < now)`. Send only when
it returns 1, then set `AlertSentAt`.
Settled: D's lease column. C's variant (claim on `AlertSentAt`, release on failure) loses the alert when a pod
dies between claim and send, and a lease simply expires. Not one long advisory-lock transaction, which
resends the whole batch after a crash. The new column is fine because the tests create the schema from the
model, and `RunOnceAsync(ct)` keeps its signature.
Skill: `worker-patterns` ("With more than one pod").

**8. One bad alert blocks every alert behind it** (C, D)
`Application/AlertSweeper.cs:26-31` (D: `:28`). One `EnsureSuccessStatusCode` failure, or a missing patient
(`phones[...]` throws `KeyNotFoundException`), ends the sweep. That reading is first in `OrderBy(Id)`, so
it blocks everything after it, every minute.
Fix: try/catch per row, log it, count attempts, and park the reading in a terminal status after N attempts.
Skill: none cited.

**9. One failing meter ends the sync and loses the whole run** (C, D)
`Application/MeterSyncService.cs:15-32` (D: `:17`). One vendor 404 throws out of the loop, and the single
`SaveChangesAsync` at the end loses every reading gathered before it.
Fix: catch per meter and log it, and save once per meter.
Skill: none cited.

**10. Two pods insert the same readings, and the unique index fails the run** (C, D, S)
`Application/MeterSyncService.cs:18,32`, `Infrastructure/SyncDbContext.cs:43`. Both pods read the same
`known` IDs and both insert the new ones.
Fix: `INSERT … ON CONFLICT ("VendorId") DO NOTHING` (an unnest batch), which also removes the `known` query.
Settled: `ON CONFLICT`, not C's advisory-lock alternative. Both pods keep syncing, and there's no lock to
hold or lose.
Skill: `postgresql-patterns` §8.

**11. An SMS timeout shuts the pod down** (C)
`Application/SyncWorker.cs:21`, `Program.cs:21-22`. `SmsClient` has no resilience handler, so a slow gateway
hits the 100 s HttpClient timeout. The `TaskCanceledException` is an `OperationCanceledException`, so it
escapes `when (ex is not OperationCanceledException)`, the worker faults, and `StopHost` stops the host.
Fix: `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)`. See also 20.
Skill: `worker-patterns`. Harness note from C: that skill's sample at line 61 uses the same filter and has
the same gap.

**12. The Arabic SMS fails on a slim image, or shows a Hijri date** (K, C, S, D)
`Application/AlertText.cs:9,13-14` (K: `:13`). Without ICU, `new CultureInfo("ar-SA")` throws in a static
field, so every alert fails with `TypeInitializationException`, and with finding 8 that blocks all alerts.
With ICU, the date is Hijri or Gregorian depending on the image, and the SMS doesn't say which. Without
tzdata, `FindSystemTimeZoneById("Asia/Riyadh")` throws.
Fix: `TakenAt.ToOffset(TimeSpan.FromHours(3))`, since Riyadh has no DST (or the lookup with a fixed +3
fallback, cached in a static). Format the date with `CultureInfo.InvariantCulture` and an explicit format.
The Arabic words stay as they are.
Settled: Critical (C, D), not K's warning, because it stops every alert. Use the invariant culture (C), not
an Arabic Gregorian culture (S, D): any `ar-*` culture still needs ICU. The invariant culture gives a
Gregorian date with Latin digits, and the patients still get their SMS in Arabic.
Skill: `localization` §6, §7 (table rows 23, 25, 26).

## High

**13. Tokens issued for other services are accepted** (K, C, S, D)
`Program.cs:25-33` (K: `:30`). `ValidateAudience = false`.
Fix: `o.Audience = "meter-sync"` with `ValidateAudience`, `ValidateIssuer` and `ValidateLifetime` on, and
`MapInboundClaims = false` (`CurrentPatient` already reads `sub`). The authority stays `Auth:Authority`.
Settled: High (S), not K's and C's warning. It is an authentication bypass across services.
Skill: `auth-patterns` §2, §8.

**14. A serial number of `..` changes the vendor path, and inputs aren't validated** (S, C)
`Infrastructure/MeterVendorClient.cs:11`, `Api/MeterEndpoints.cs:27`. `Uri.EscapeDataString` leaves `.`
and `..` as they are, so `meters/../readings` reaches another vendor path, and its response is stored as
that patient's readings. `NationalId` can be null or too long (C).
Fix: return 400 unless the serial matches `^[A-Za-z0-9-]{1,40}$` and the national ID matches `^[12]\d{9}$`.
Skill: `rules/security.md` (input validation).

**15. A sync failure stops alerts too** (C, D)
`Application/SyncWorker.cs:14-19`. Sync and sweep share one `try`, so while the vendor fails, readings
already stored are never texted.
Fix: give each step its own try, or its own worker.
Settled: High, between C's warning and D's critical. Once 6 and 9 are fixed only a real vendor outage
triggers it, but then it stops patient alerts.
Skill: none cited.

**16. A newly linked meter texts its whole history** (C, D)
`Application/MeterSyncService.cs:27`. The vendor returns every reading ever uploaded, so linking a meter
texts the patient about months-old readings, possibly a previous owner's.
Fix: alert only for `TakenAt >= LinkedAt` (or a recent window). Store older readings with no alert.
Rated High here (both reviewers said warning) because a previous owner's glucose data reaches someone
else's phone.
Skill: none cited.

## Medium

**17. No foreign keys, no index on `MeterId`, and an unbounded read per meter** (C, D)
`Infrastructure/SyncDbContext.cs:24-45`, `Application/MeterSyncService.cs:13-18`. With no relationships
configured, EF creates no foreign keys and no index. Each minute is a sequential scan per meter that loads
every vendor ID it ever had. With many meters, a run outlasts the 1-minute tick.
Fix: `HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId)`, the same for `PatientId` and for
`Meter`→`Patient`. Add `CREATE INDEX CONCURRENTLY ix_readings_meter_id` and `ix_readings_patient_id`.
Finding 10 removes the `known` query (C's `VendorId IN (…)` is the alternative).
Skills: `efcore-patterns` §9/§11, `postgresql-patterns` §11.

**18. Two concurrent links of one serial return 500, not 409** (C, S, D)
`Api/MeterEndpoints.cs:41-48`. Both pass `AnyAsync`, and the unique index throws `DbUpdateException`.
Fix: catch `PostgresException { SqlState: "23505" }` and return Conflict.
Skill: `api-design` §2.

**19. No exception handler or ProblemDetails** (S, C)
`Program.cs:44-49`. A Production 500 has no body and no trace id.
Fix: `AddProblemDetails()` and `UseExceptionHandler()`, with no internals in the response.
Skill: `api-design` §2.

**20. The SMS client has no timeout** (C, S)
`Program.cs:21-22`. Fix: a total timeout, and no retry: a retried POST sends a second SMS (`VENDOR.md:19`).
Use `DisableForUnsafeHttpMethods` if a standard handler is added.
Skill: `polly-resilience` (lines 47, 60).

**21. Scope validation is off in Production** (K, C, S, D)
`Program.cs:9`. Fix: `UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })`.
It would have caught 5 at startup.
Settled: in the same change as 5, not before (D), or startup fails.
Skill: `dotnet-core`.

**22. A missing or non-numeric `sub` gives a 500** (C, S)
`Api/CurrentPatient.cs:9`. `!` plus `int.Parse`. Fix: `int.TryParse` and return 401.
Skill: `auth-patterns` §5.

**23. Health data in logs** (C, S, D)
`Application/MeterSyncService.cs:23` logs the glucose value. `SyncWorker.cs:23` logs the whole exception,
and the default HttpClient logging writes the vendor URL with the meter serial (S).
Fix: log the vendor id only, and the exception type and status code.
Settled: Medium (C, D), not S's low. `rules/security.md` bans health records in logs.
Skill: `pii-masking`.

**24. Connection pool not sized for several pods** (D)
`Program.cs:11`. The default 100 connections per pod can exceed PostgreSQL's 100.
Fix: set `Maximum Pool Size` (about 20, agreed with the DBA) and `EnableRetryOnFailure`.
Settled: set it in code with `NpgsqlConnectionStringBuilder`, not in `ConnectionStrings:Sync` as D
suggested at `appsettings.json:3`, because task.md fixes that setting as production has it.
Skill: `postgresql-patterns` §3.

**25. A long vendor `value` or `unit` fails the batch** (C)
`Infrastructure/SyncDbContext.cs:37-38`. Over 16 or 8 characters. Fix: validate before saving, and park the
item.
Skill: none cited.

**26. Config read with `!`** (C)
`Program.cs:19,22,27`. Fix: bound options with `ValidateOnStart`. The production values must pass,
including the base address without a trailing `/` (see 6).
Skill: `dotnet-core`.

**27. No security headers, HSTS or forwarded headers** (S)
`Program.cs`. Fix: headers in `OnStarting`, HSTS for 365 days, and `UseForwardedHeaders` behind nginx.
Skill: `owasp-aspnetcore` §1-2.

**28. No rate limit on `POST /meters`** (S)
Fix: a partitioned limiter keyed on `sub`.
Skill: `api-design` §6.

**29. Vulnerable packages aren't blocked, and nobody ran the scan** (S)
No `Directory.Build.props` with `NuGetAudit`, `NuGetAuditMode=all`, NU1901-NU1904 as errors. Run
`dotnet list package --vulnerable --include-transitive`. Any hit blocks the release.
Skill: `zero-vulns`.

**30. No reviewed schema scripts** (C, D)
Fix: an up/down script pair for the first deployment, including the foreign keys, indexes and claim column.
Keep never calling `Migrate`/`EnsureCreated`. Later indexes on `readings` need `IsCreatedConcurrently()`
and no `--idempotent`.
Skill: `rules/efcore-rules.md`.

**31. Layer rules broken** (C)
`Infrastructure/PatientDirectory.cs:1,8` depends on `Api.MeterResponse`, and Application uses `SyncDbContext`
directly.
Fix: move the response mapping to the Api layer, and put repository interfaces between Application and EF.
Skill: `rules/architecture.md`.

## Low

**32. Partial index for unsent alerts** (C, D)
`Infrastructure/SyncDbContext.cs:44`. Replace `(Alert, AlertSentAt)` with
`CREATE INDEX CONCURRENTLY ix_readings_alert_due ON readings ("Id") WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL`
(`HasFilter`).
Skill: `postgresql-patterns`.

**33. Vendor responses have no size cap** (S)
`Infrastructure/MeterVendorClient.cs:11`. Fix: set `MaxResponseContentBufferSize`.
Skill: none cited.

**34. The Arabic SMS sends as two parts** (C)
`Application/AlertText.cs:16`. Over 70 characters. Fix: shorten it, if the cost matters.
Skill: `localization` §10.

**35. The SMS carries the glucose value to the phone** (S)
This is the stated design. The compliance auditor signs off on it.
Skill: `healthcare-compliance` (compliance-auditor).

**36. DBA minor points** (D)
`Api/MeterEndpoints.cs:35` loads a tracked patient only to read its id: use `.Select(p => p.Id)`. Use
`UseIdentityAlwaysColumns()` (house rule; Npgsql defaults to BY DEFAULT). PascalCase column names need quotes
in hand-written SQL.
Skill: `rules/efcore-rules.md`.

**37. `Patient` and `Meter` have no behavior** (C)
`Domain/Patient.cs`, `Domain/Meter.cs`. Fix: move state changes into methods. Keep the
`new Patient(nationalId, phone)` constructor.
Skill: `ddd-patterns`.

**38. No tests** (C)
Fix: unit tests in a sibling `MeterSync.Tests/`, at least for findings 1, 2, 7 and 12.
Skill: `testing-tdd`.

## Checked and fine
`GET /meters` filters by the caller. The fallback policy denies by default, and `ClockSkew` is 30 s.
`TimeProvider` and `PeriodicTimer` are used. Read queries use `AsNoTracking`. Every string column is bounded,
timestamps are `timestamptz`, values are `numeric(6,1)`, and keys are `int`. Nothing calls `Migrate` or
`EnsureCreated`. Numbers parse with the invariant culture. Sent alerts are saved one at a time. The SMS POST
isn't retried.

## Not checked
No reviewer built or ran anything. The vulnerable-package scan, query plans (no database), and whether the
claim design passes the hidden two-instance tests were not checked. The Roslyn MCP wasn't used (13 small
files, all read).
