# ClinicQueue: .NET 8 → .NET 10

The service now targets `net10.0` and builds with warnings as errors: 0 warnings, and no vulnerable or deprecated packages, direct or transitive. 18 unit tests in `ClinicQueue.Tests/` pass on .NET 10. They run the app in-process in Production, with requests coming from 10.42.0.7 with `X-Forwarded-For` and tokens signed by the test. Nothing that needs PostgreSQL was run here.

## Preflight (`dotnet-upgrade`)

The preflight script needed a permission that wasn't granted, so I went through its checks by hand against every file:

| Check | Where | Result |
|---|---|---|
| `binary-formatter` (breaks) | `Application/QueueSnapshots.cs` | Fixed. It threw on every cache hit on .NET 9 and later, which a test proved. |
| `system-linq-async` (breaks) | `ClinicQueue.csproj`, `QueueSnapshots.cs` | Fixed. |
| `forwarded-known-networks` (obsolete) | `Program.cs` | Fixed. |
| `forwarded-headers-trust` (behaviour) | `Program.cs` | Was already right (pod network named, `ForwardLimit = 2`). Tests now pin it. |
| `exception-handler-diagnostics` (behaviour) | `Application/ErrorHandler.cs` | Fixed. A test proved the error was missing from the logs. |
| `background-service-start` (behaviour) | `Application/DirectoryWarmer.cs` | Fixed. A test proved `/clinics` came back empty after start. |
| `npgsql-date-read` (check) | `Application/VisitReads.cs` | Fixed. `(DateTime)` on a `DateOnly` would have thrown. |
| `npgsql-connection-lifetime` (behaviour) | connection string | Noted only. Connections now recycle after an hour, which is harmless. |
| `versionless-package`, `with-openapi`, `webhost-builder`, `action-context-accessor`, `ef-hasdata-dynamic`, `image-tag` | — | Not present. There is no Dockerfile in the folder. |

## Changes

### The upgrade

| File | What | Why |
|---|---|---|
| `global.json` (new, repo root) | SDK `10.0.100` with `rollForward: latestFeature`. | Pins the build to the 10 SDK. |
| `ClinicQueue/ClinicQueue.csproj` | `net8.0` → `net10.0`. JwtBearer 8.0.11 → 10.0.15. Npgsql EF provider 8.0.10 → 10.0.3. | The target framework and the framework/EF packages move together. |
| `ClinicQueue/ClinicQueue.csproj` | Removed `System.Linq.Async`. | .NET 10 has async LINQ built in, and with the package referenced too the calls become ambiguous. |
| `ClinicQueue/ClinicQueue.csproj` | Removed `EnableUnsafeBinaryFormatterSerialization` and `NoWarn SYSLIB0011`. | BinaryFormatter always throws from .NET 9, so the switch does nothing now. |
| `ClinicQueue/ClinicQueue.csproj` | Removed `WarningsNotAsErrors NU1901–NU1904`. Added `NuGetAudit`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`, and NU1901–NU1904 as errors. | A known-vulnerable package, direct or transitive, now fails the build instead of passing with a warning. |
| `Application/QueueSnapshots.cs` | BinaryFormatter → `System.Text.Json`. Removed `[Serializable]`. | BinaryFormatter is removed. The cache is in memory with a one-minute expiry, so no old-format entry survives a deploy. |
| `Application/QueueSnapshots.cs` | `AsAsyncEnumerable().Select().ToArrayAsync()` → EF `.Select().ToArrayAsync()`. | Removes the `System.Linq.Async` dependency. The projection now runs in SQL and gives the same result. |
| `Program.cs` | `KnownNetworks.Add(new HttpOverrides.IPNetwork(...))` → `KnownIPNetworks.Add(System.Net.IPNetwork.Parse(...))`. It fails fast if `Proxies:Network` is missing. | The old API is obsolete on 10, an error under warnings-as-errors. Same trust: 10.42.0.0/16, `ForwardLimit = 2`. |
| `Application/ErrorHandler.cs` | Logs the exception (method, path, trace id) before writing the 500. | On .NET 10 a handler that returns `true` stops the middleware's own error log, so errors were dropping out of the logs. |
| `Application/DirectoryWarmer.cs` | The first `Refresh()` moved to `StartAsync`, which the host awaits. | On 10 all of `ExecuteAsync` runs in the background, so the first requests saw an empty clinic list. |
| `Application/VisitReads.cs` | `ExecuteScalar` + `(DateTime)` cast → reader + `GetFieldValueAsync<DateTime>`. | Npgsql 10 returns an untyped `date` as `DateOnly`, so the cast would throw. Every `GET /visits/{id}/day` would have been a 500. |

### From `/full-review`

| File | What | Why (found by) |
|---|---|---|
| `Application/VisitBooking.cs` (new), `Api/QueueEndpoints.cs`, `Program.cs` | Booking moved out of the endpoint. The count and insert now run in one transaction under `pg_advisory_xact_lock(clinicId, dayNumber)`. | **Critical**, found by all three reviewers. Two bookings at the same moment both got position count+1. The lock needs no schema change, and it is released at commit. It also takes DbContext use out of the endpoint (architecture rule). Positions are still count+1. |
| `Application/VisitBooking.cs` | After a booking, removes the cached queue snapshot for that clinic and day. | Code review: the screens showed a stale queue for up to a minute. This only clears the local pod's in-memory cache. |
| `Application/DirectoryWarmer.cs` | A failed periodic refresh is logged and the previous list is kept: `catch … when (!stoppingToken.IsCancellationRequested)`. | known-traps scan, confirmed by all reviewers. One DB timeout during a refresh would have stopped the host (`StopHost`). |
| `Application/QueueSnapshots.cs` | The cache key is built with `InvariantCulture` (`KeyFor`). | DBA and code review: under `ar-SA` the key could switch to the Hijri calendar. |
| `Api/QueueEndpoints.cs` | `int.Parse(sub!)` → `int.TryParse`, giving 403 when `sub` is missing or not an integer. | Security and code review: a valid token with a UUID `sub` gave a 500, and the raw subject went into the error log. |
| `Program.cs` | JWT `ClockSkew` set to 30 seconds. | Security rule (the default is 5 minutes). |
| `Program.cs` | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` on every response. `UseHsts()` outside Development. | Security review: there were no security headers. HSTS is only sent on HTTPS requests, which here means `X-Forwarded-Proto: https`. |

### Tests: `ClinicQueue.Tests/` (new, 18 tests)

The tests run the app the way production does: `WebApplicationFactory<Program>`, Production, requests from 10.42.0.7, and `IClinicSource` and the cache replaced. Before each fix, the matching test was seen failing:
- Without the fixes, on the .NET 10 runtime: the BinaryFormatter cache hit, the missing error log, and the empty `/clinics` after start.
- After the upgrade, before the review fixes: the 403 for a bad `sub`, the 30-second skew, and the security headers.

The other tests pin current behaviour:
- `/whoami` behind nginx, including a spoofed `X-Forwarded-For`
- the `/clinics` and queue JSON shapes
- 401 without a token, `/health/live` without a token
- the EF model against the `clinic`/`visit` columns (`GenerateCreateScript`), and `day` sent as a `date` parameter

One test proves nothing here: the ar-SA cache key test passed even before its fix, because this machine's ICU gives ar-SA a Gregorian calendar. It stays as a guard.

## Not tested here (needs PostgreSQL)

- `POST /visits` with the advisory lock: the SQL `SELECT pg_advisory_xact_lock(@p0, @p1)` with two int4 values.
- `VisitReads` reading `date` on Npgsql 10.
- `EfClinicSource`.

Line coverage is 61.6% and branch coverage 25%, both below the 80% / 70% targets. The uncovered lines are these DB paths. Integration tests against a real PostgreSQL (Testcontainers) should cover them.

## Found but not changed

**Would change a contract, behaviour or production setting the tests rely on:**
- **No limit on bookings.** Any patient can book any clinic, for any day (past or far future), any number of times. There is no rate limit on `POST /visits` or the queue GET, and any `id`/`day` pair misses the cache and runs a query. A booking window, a per-`sub` rate limiter and a check against known clinics are product decisions, and they would change today's responses.
- **`Proxies:Network` trusts the whole pod network (10.42.0.0/16).** Any pod can set `X-Forwarded-For` itself. Nothing makes access decisions on the IP today. Narrow the value or add a NetworkPolicy. The setting was kept as production has it.
- **Scope validation (`ValidateScopes`/`ValidateOnBuild`) is still off in Production** (known-traps, info). There is no captive dependency today. I left it off because it makes resolving a scoped service from the root provider throw, and test harnesses often do exactly that (`factory.Services.GetRequiredService<QueueDbContext>()`).
- **The realm.** `Auth:Authority` points at a `staff` realm while `sub` is a patient id. Confirm which tokens are meant to book.

**Need the DBA or a schema change:**
- A unique index on `visit (clinic_id, day, position)`, built `CONCURRENTLY`. Check for duplicates already left by the race first: `SELECT clinic_id, day, position FROM visit GROUP BY 1,2,3 HAVING count(*) > 1`.
- Whether production has the `(clinic_id, day)` index the model declares. There are no migrations in the repo.
- An FK from `visit.clinic_id` to `clinic`.

**Cleanups and suggestions left for a separate change, so the upgrade diff stays focused:**
- One `NpgsqlDataSource` shared by EF and `VisitReads`, instead of two pools per pod, and a `Maximum Pool Size` sized for pods × size < `max_connections`.
- `SSL Mode=VerifyFull` on the DB connection (it comes with the secret).
- `Visit.Day` / `QueueSnapshot.Day` as `DateOnly`, which is Npgsql 10's default for `date`.
- `QueueSnapshot` as an immutable record.
- `ErrorHandler` moved to `Api/`.
- Logging of auth failures and bookings.
- `IClinicSource.Load()` is synchronous DB I/O on the warmer thread. That is fixed by contract.
- Leftover `bin/`/`obj/` net8.0 output in `ClinicQueue/`. It is harmless and gone after a clean.

**Upgrade notes from the release notes that may matter in the cluster:**
- The base image should move to the 10.0 tag. 10 images are Ubuntu by default, so check ICU/tzdata.
- Npgsql 10 validates TLS against the given root CA only, and renames its metrics and spans.
- EF Core 10 renames SQL parameters, so cached plans recompile once after the deploy.
