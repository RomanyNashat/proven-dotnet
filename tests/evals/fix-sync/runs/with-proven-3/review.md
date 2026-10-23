# MeterSync review: consolidated

Sources: C = code review, S = security review, D = DBA review, K = known-traps scan (`scan.md`).
All four agree the service is not ready for production. 33 findings, most severe first. Every line
number is in `MeterSync/`. "Settled" lines resolve a disagreement between sources, within what
`task.md` says must stay (routes and shapes, `Patients`, `new Patient(nationalId, phone)`,
`SyncOnceAsync`/`RunOnceAsync`, typed clients, `TimeProvider`, JWT with audience `meter-sync`, and the
production settings as given).

## Critical

**1. `HI` and `LO` readings are dropped.** C, S, D
`Application/MeterSyncService.cs:21-25`. `HI` (over 600 mg/dL) and `LO` (under 20) fail
`decimal.TryParse` and are skipped, so the most dangerous readings never alert. They are re-fetched and
logged as a warning every minute.
Fix: map `HI` to High and `LO` to Low, keep `Raw`.
Settled: store the meter's bound (600 / 20) in `ValueMgDl` rather than making it nullable (D offered
both). The column stays non-null, `numeric(6,1)` holds 600, and the SMS already shows `Raw`.
Skill: none cited (VENDOR.md:12-13).

**2. `mmol/L` values are treated as mg/dL.** C, S, D
`Application/MeterSyncService.cs:27`, `Domain/Reading.cs:50`. `unit` is ignored: 5.5 mmol/L (normal)
gets a "low" SMS, and 20 mmol/L (360 mg/dL, high) is flagged Low.
Fix: multiply by 18 for `mmol/L`, reject unknown units, and do the conversion and classification in
`Reading.FromMeter`.
Skill: none cited (VENDOR.md:14).

**3. Every alert SMS goes out twice on two pods.** C, S, D
`Application/AlertSweeper.cs:16-31`. Both pods select the same unsent rows and both POST; the gateway
has no idempotency key. An SMS whose `SaveChanges` then fails is also sent again.
Fix: claim rows atomically before sending.
Settled: claim with `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT 100) RETURNING` plus a
claimed-at lease, not one sweeper under an advisory lock (C and D offered both). The claim row also
carries the attempt count from finding 4, and both pods share the work. Mark sent after the POST
succeeds: no duplicates in normal running, and a resend only if a pod dies between POST and save. Say
so in the code.
Skill: `worker-patterns` :88-121, `postgresql-patterns` §11, `outbox`.

**4. One failing alert blocks every alert behind it.** C, S, D
`Application/AlertSweeper.cs:26-31` (`:28`). `phones[...]` throws `KeyNotFoundException` (no FK from
`Reading.PatientId`, see 17), and a 4xx from the gateway aborts the loop. Rows are taken
`OrderBy(Id)`, so the same row fails first every minute.
Fix: try/catch per reading, record attempts and the last error, park the row after N attempts.
Skill: none cited.

**5. One failure loses the whole sync run.** C, D
`Application/MeterSyncService.cs:18-32`. A single `SaveChangesAsync` covers every meter. A duplicate
`VendorId` inserted by the other pod (unique index, `Infrastructure/SyncDbContext.cs:43`), one vendor
error, or one over-long `Raw`/`Unit`/`VendorId` (16/8/64) loses every meter's readings for that run,
and a persistent one loses them every minute.
Fix: fetch and save per meter; insert with `INSERT … ON CONFLICT ("VendorId") DO NOTHING`.
Settled: critical (D), not a warning (C). On two pods, which task.md says production runs, a stuck
meter or a race stops every patient's readings.
Skill: `postgresql-patterns` §8, §11.

**6. Any patient can link a meter to anyone (IDOR).** C, S, D
`Api/MeterEndpoints.cs:35` (`:24-48`). The patient comes from the body's `nationalId` and is never
checked against the caller's `sub`. An attacker can take a victim's meter (serial printed on the
device) and receive their readings and SMS, and 404 vs 201/409 tells them which national IDs are
patients.
Fix, body shape unchanged: `db.Patients.SingleOrDefaultAsync(p => p.Id == callerId && p.NationalId == request.NationalId, ct)`;
a mismatch is 404.
Follow-up (S, not fixed here): nothing proves the caller owns the meter. Ask the vendor for a pairing
code.
Skill: `auth-patterns` §4, `api-design` §11, `rules/security.md`.

**7. Singleton `PatientDirectory` holds the scoped `SyncDbContext`.** K, C, S, D
`Program.cs:13`. Every `GET /meters` shares one context; concurrent calls throw "A second operation was
started", and the context is never disposed.
Fix: `AddScoped<PatientDirectory>()`. (`AlertSweeper` at `:15` is fine as a singleton: it takes
`IServiceScopeFactory`.)
Skill: `dotnet-core`, `design-patterns`.

**8. CORS allows every origin with credentials.** K, C, S, D
`Program.cs:38-42`. `SetIsOriginAllowed(_ => true)` with `AllowCredentials`; `Cors:Origins` exists and
nothing reads it.
Fix: `.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()!)`,
`.WithMethods("GET","POST")`, `.WithHeaders("Authorization","Content-Type")`, and drop
`AllowCredentials`: the API uses bearer tokens, not cookies.
Skill: `owasp-aspnetcore` §3 (:99, :111, :262).

**9. Vendor calls lose `/v2`.** K, C, S, D
`appsettings.json:6`, `Program.cs:19`, `Infrastructure/MeterVendorClient.cs:11`. `https://meters.example.net/v2`
plus `meters/...` resolves to `/meters/...`.
Fix: in code at `Program.cs:19`, append `/` when missing (or bind validated options that normalise it).
Settled: fix in code, not in appsettings as K suggests, because task.md fixes production's value
without the slash. Raised from K's warning to critical: every vendor call misses, so no reading is ever
synced.
Skill: `dotnet-core` :72, :113.

**10. A slow SMS gateway stops the host.** C, S, D
`Application/SyncWorker.cs:21`, `Program.cs:21-22`. `SmsClient` has no timeout handler, so
`HttpClient.Timeout` surfaces as `TaskCanceledException`. That is an `OperationCanceledException`, so
it escapes `when (ex is not OperationCanceledException)`, faults `ExecuteAsync`, and the default
`StopHost` takes the service down.
Fix: catch `when (!stoppingToken.IsCancellationRequested)`, and give the SMS client a timeout with no
retries (`DisableForUnsafeHttpMethods()`): a POST sends a message each time.
Skill: `polly-resilience` :47-62.

## High

**11. The token audience is not checked.** K, C, S, D
`Program.cs:30`. `ValidateAudience = false`; the authority is a shared realm, so a token issued to any
other client there is accepted.
Fix: `ValidateAudience = true`, `ValidAudience = "meter-sync"`, keep `ClockSkew` at 30 s.
Settled: high (S), not a warning (K, C): any token from the shared realm gets in, and task.md requires
this audience anyway.
Skill: `auth-patterns` §1 (:51), §8 (:347).

**12. The SMS text breaks without tzdata or ICU, and the date may come out Hijri.** K, C, S, D
`Application/AlertText.cs:13` (K); `:9`, `:14` (C, S, D). A bare `FindSystemTimeZoneById` throws
without tzdata. A static `new CultureInfo("ar-SA")` throws without ICU and breaks the type initializer
for good. With ICU, `ar-SA` formats Hijri or Gregorian depending on the image, under a `dd/MM/yyyy`
that looks Gregorian.
Fix: `RiyadhTime` with a fixed UTC+3 fallback, resolved once; `Cultures.ArabicGregorian` for the date.
Settled: high (S), not a warning (K, C). On an image without either, every sweep fails and no patient
gets an alert.
Skill: `localization` §6, §7.

## Warning

**13. Concurrent links return 500 instead of 409.** C, S, D
`Api/MeterEndpoints.cs:41-48`. Check-then-insert; the second insert hits the unique index.
Fix: catch `PostgresException { SqlState: "23505" }` and return `Conflict`.
Skill: none cited.

**14. A newly linked meter texts its whole history.** C, D
`Application/MeterSyncService.cs:17-27`. The vendor returns every reading (VENDOR.md:3), so old
out-of-range readings go out 100 a minute.
Fix: store the history, but alert only on readings taken after `LinkedAt`, or within a recent window.
Skill: none cited.

**15. A vendor outage also stops alerts already stored.** C, D
`Application/SyncWorker.cs:14-19`. Sync and sweep share one try.
Fix: a try for each.
Skill: none cited.

**16. N+1 query over an unindexed column.** C, D
`Application/MeterSyncService.cs:18`, `Infrastructure/SyncDbContext.cs:33-45`. One query per meter
loads every known `VendorId`, and `readings."MeterId"` has no index, so each is a sequential scan every
minute.
Fix: query `Where(r => vendorIds.Contains(r.VendorId))` against the unique index, or rely on
`ON CONFLICT` (finding 5) and drop the read; add `HasIndex(r => r.MeterId)`.
Skill: `efcore-patterns`, `postgresql-patterns`.

**17. No foreign keys.** D (C notes it under 4)
`Infrastructure/SyncDbContext.cs:24-45`. No FK on `meters.PatientId`, `readings.MeterId` or
`readings.PatientId`, so orphan rows are possible.
Fix: `HasOne<Patient>().WithMany().HasForeignKey(...)` (EF indexes the FK columns). Tests add patients
first and build the schema from the model, so this stays within task.md.
Skill: `efcore-patterns`; script with `NOT VALID` then validate, `postgresql-patterns` §5.

**18. A missing or non-numeric `sub` gives a 500.** C, S
`Api/CurrentPatient.cs:9`. `!` hides null and `int.Parse` throws.
Fix: `int.TryParse`.
Settled: return 401 (C offered 401/403, S 401/404). A token without a usable patient id is not an
identity this API accepts.
Skill: `auth-patterns` §5.

**19. No exception handler, ProblemDetails, security headers or HSTS.** C, S
`Program.cs:44-49`. Nothing sends `Cache-Control: no-store`, so patient data can sit in shared caches.
Fix: `AddProblemDetails` + `UseExceptionHandler`, `UseApiSecurityHeaders()` first, `AddHsts` (365 days)
with ForwardedHeaders behind nginx.
Skill: `owasp-aspnetcore` §1, §2; `api-design` §2.

**20. Settings fail on first use, not at startup.** C
`Program.cs:19,22`. `Configuration[...]!`.
Fix: validated options with `ValidateOnStart` (this can also normalise the slash in finding 9).
Skill: `dotnet-core`.

**21. Connection pool and retries are unset.** D
`Program.cs:11`. Several pods at Npgsql's default of 100 connections exceed PostgreSQL's 100
`max_connections`.
Fix: set `Maximum Pool Size` in code with `NpgsqlConnectionStringBuilder` (the production connection
string stays as given), sized with the DBA. If `EnableRetryOnFailure` is added, run the claim
transaction in finding 3 inside `CreateExecutionStrategy()`.
Skill: `postgresql-patterns` §3, `rules/performance.md`.

**22. Glucose values in logs.** S, D
`Application/MeterSyncService.cs:23` logs the raw value beside the vendor id. Also
`Application/SyncWorker.cs:23` logs whole exceptions, which today carry no phone number but could.
Fix: log `VendorId` only.
Settled: warning (D), not low (S): `rules/security.md` bans health records in logs.
Skill: `pii-masking`.

**23. Serial numbers are case-sensitive.** D
`Api/MeterEndpoints.cs:41`. `abc1` and `ABC1` are two meters in PostgreSQL.
Fix: normalise the serial before saving and comparing.
Skill: `postgresql-patterns` §2.

**24. No per-caller rate limit on `POST /meters`.** S
`Api/MeterEndpoints.cs`. Finding 6 removes most national-ID guessing; limit anyway.
Fix: `AddRateLimiter` partitioned by `sub`.
Skill: `api-design` §6.

**25. Vulnerable packages not checked, NuGetAudit off.** S
`MeterSync.csproj`. All packages at `10.0.0`; nobody ran the scan.
Fix: `dotnet list package --vulnerable --include-transitive` before merge (any hit blocks), and
`NuGetAudit`, `NuGetAuditMode=all`, `NU1901-NU1904` as errors.
Skill: `zero-vulns`.

## Info

**26. Scope validation is off in Production.** K, C, S, D
`Program.cs:9`. Fix: `UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })`,
so finding 7 fails at startup. Skill: `dotnet-core` :18-41.

**27. The sweeper's index should be partial.** C, D
`Infrastructure/SyncDbContext.cs:44`. Fix: replace `(Alert, AlertSentAt)` with
`HasIndex(r => r.Id).HasFilter("\"Alert\" <> 'None' AND \"AlertSentAt\" IS NULL")`, created
`CONCURRENTLY` by script. Skill: `postgresql-patterns`.

**28. `NationalId` is not validated.** S
`Api/MeterEndpoints.cs` (`LinkAsync`). Fix: exactly 10 digits, as a `ValidationProblem`. Skill:
`owasp-aspnetcore`.

**29. Layering.** C
`Infrastructure/PatientDirectory.cs:1` references `MeterSync.Api`; `LinkAsync` uses `SyncDbContext`
directly with its business logic in the endpoint. Skill: `rules/architecture.md`.

**30. PII in plaintext, no audit log of links.** C, S
`Domain/Patient.cs:7-9`. `NationalId` and `Phone` are stored unencrypted; meter links aren't audited.
Settled: record as a decision, don't fix now. Encrypting `NationalId` needs a deterministic scheme or
a hash for the lookup, and the tests add patients through the constructor. Skill: `rules/security.md`.

**31. Naming and identity conventions.** D
`Infrastructure/SyncDbContext.cs`. snake_case tables with quoted PascalCase columns; keys use `BY DEFAULT`
identity where the rule is `UseIdentityAlwaysColumns()`. Skill: `postgresql-patterns` §2,
`rules/efcore-rules.md`.

**32. `Patient` and `Meter` have no behaviour.** C
`Domain/Patient.cs`, `Domain/Meter.cs`. Skill: `rules/csharp-standards.md`.

**33. The Arabic SMS is two parts.** C
`Application/AlertText.cs:14`. Over 70 characters in UCS-2, so each alert costs two messages. Skill:
`localization` §10.

## Shipping the schema changes (D)

There are no migrations and no `Migrate()`. The changes above (claim columns, FKs, indexes) are
additive and safe with old pods running. Ship them as reviewed scripts: concurrent indexes on their own
without `--idempotent`, `SET lock_timeout` first, FKs `NOT VALID` then validated
(`postgresql-patterns` §5).

## Not checked (all sources)

Nothing was built or run; no `EXPLAIN`, no vulnerable-package scan, no image contents (ICU, tzdata).
The Roslyn MCP wasn't available. No source found text in the repo addressed to an AI reviewer.

## For the harness, not the service

- C: the `worker-patterns` :61 sample uses the same `when (ex is not OperationCanceledException)`
  filter as finding 10. Check the skill.
- S: a POST that takes a business identifier in the body (here a national ID) is an IDOR shape the
  known-traps scan doesn't flag. The fallback policy only proves the caller signed in.
