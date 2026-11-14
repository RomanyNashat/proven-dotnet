# ClinicQueue: .NET 8 → .NET 10

The service builds on .NET 10 with warnings as errors and has no known-vulnerable or deprecated packages.
17 unit and in-process tests pass in `ClinicQueue.Tests/` (there were none before). Nothing that needs
PostgreSQL could run here: see "Not proven here" at the end.

## Changes

| File | What | Why |
|---|---|---|
| `ClinicQueue.csproj` | `net8.0` → `net10.0`; JwtBearer 8.0.11 → 10.0.0; Npgsql EF provider 8.0.10 → 10.0.0 | The framework, its packages and EF Core move together (C# 14 and EF Core 8 don't mix). |
| `ClinicQueue.csproj` | Removed `System.Linq.Async` | .NET 10 has async LINQ built in, and the old package makes calls ambiguous. The one use now runs in the database instead (below). |
| `ClinicQueue.csproj` | Removed `EnableUnsafeBinaryFormatterSerialization` and `NoWarn SYSLIB0011` | BinaryFormatter always throws from .NET 9 on. The code that used it is gone. |
| `ClinicQueue.csproj` | `NU1901`–`NU1904` are errors now, not exempted; added `NuGetAuditMode=all` | Under the old setting, a known-vulnerable package could ship. Now one in the whole package graph fails the build. |
| `Application/QueueSnapshots.cs`, new `QueueSnapshot.cs`, new `QueueSnapshotCache.cs` | Snapshots are cached as System.Text.Json, not BinaryFormatter. An entry it can't read counts as a miss. The positions query projects and runs `ToArrayAsync` in EF. | **On .NET 10, `GET /clinics/{id}/queue` threw on every call.** Deserializing cache bytes with BinaryFormatter was also a remote-code-execution risk. The JSON route shape is unchanged. |
| `Domain/Visit.cs`, `Api/QueueEndpoints.cs` | `Visit.Day` and the parsed day are `DateOnly`, not `DateTime`. The column is still `date`. | Npgsql 10 maps `date` to `DateOnly`, and this removes `DateTime` Kind ambiguity. The output format `yyyy-MM-dd` is unchanged. |
| `Application/VisitReads.cs` | Reads the day with `GetFieldValueAsync<DateOnly>`, using typed positional parameters. Uses the shared `NpgsqlDataSource` instead of `new NpgsqlConnection` per call. | **On Npgsql 10, `ExecuteScalar` returns a `date` as `DateOnly`, so the old `(DateTime)value` cast threw. `GET /visits/{id}/day` returned 500.** The ownership filter (`patient_id`) is kept. |
| `Program.cs` | `KnownNetworks` + `HttpOverrides.IPNetwork` → `KnownIPNetworks` + `System.Net.IPNetwork.Parse(Proxies:Network)` | The old API is obsolete on ASP.NET Core 10 (ASPDEPR005), which is a build error under warnings as errors. `ForwardLimit = 2` is kept for ingress → nginx → app. Tested: `/whoami` returns the real client IP behind nginx (10.42.0.7), and an extra spoofed `X-Forwarded-For` entry is ignored. |
| `Application/DirectoryWarmer.cs` | The first directory fill moved from the top of `ExecuteAsync` into `StartAsync` | **On .NET 10, the code in `ExecuteAsync` no longer runs before start-up completes, so the first requests got an empty `/clinics`.** The host awaits `StartAsync`, so the list is filled before traffic. A unit test failed with the old placement and passes now. |
| `Application/DirectoryWarmer.cs` | A failed periodic refresh is logged and retried on the next tick. The timer uses the injected `TimeProvider`. | Before, one failed refresh stopped the whole host (the default for `BackgroundServiceExceptionBehavior`). The catch lets cancellation through only at shutdown (known-traps finding). A failure on the first fill still stops start-up, as before. |
| `Application/ErrorHandler.cs` | Logs the exception itself (source-generated `LoggerMessage`) | **On .NET 10, a handler that returns `true` suppresses the middleware's own error log, so 500s vanished from logs and APM.** Tested with `FakeLogger`. |
| `Application/ErrorHandler.cs` | `TryWriteAsync` instead of `WriteAsync` | `WriteAsync` threw when the client's `Accept` header matched no writer (e.g. `text/html`), and the client got an empty 500 (code review). Test was red before, green after. |
| new `Application/VisitBookings.cs`, `Api/QueueEndpoints.cs` | A booking runs in one transaction, behind `pg_advisory_xact_lock(clinicId, day)`: check the clinic, count, insert, commit. | **Two bookings at the same moment (from any pod) got the same queue position** (all three reviewers). No table change is needed. The position rule (`COUNT + 1`) and the 201/400 responses are unchanged. |
| `Api/QueueEndpoints.cs` | After a booking, the cached snapshot for that clinic and day is dropped | This pod's waiting-room screen shows the booking at once. Other pods have their own memory cache and catch up within the cache's one minute, as before. |
| `Api/QueueEndpoints.cs` | A `sub` claim that is missing or not a number gets 403 | Before, `int.Parse` turned it into a 500. Numeric `sub` behaves as before. |
| `Program.cs` | `NpgsqlDataSource` is a singleton shared by EF (`UseNpgsql(dataSource)`) and `VisitReads`. The connection string, `Proxies:Network` and `Auth:Authority` are read when services are built, not at the top of `Program`. | One pool per pod. A host or test that adds configuration (`ConnectionStrings:Queue`, …) is honoured whichever way it adds it. |
| `Program.cs` | JWT options: same authority key, audience and `MapInboundClaims=false`; `ClockSkew` 30 s; `Validate(...).ValidateOnStart()` on the authority | Without an authority the pod started, passed its liveness probe, and answered 401 to everything (code and security review, known-traps). The skew follows the team rule. |
| `Program.cs` | `ValidateScopes` and `ValidateOnBuild` on in every environment | A future captive dependency stops the start in Production too (known-traps). No captive dependency exists today. |
| `Program.cs` | `UseExceptionHandler` first; `X-Content-Type-Options`, `X-Frame-Options` and `Content-Security-Policy` on every response | Exceptions in the forwarded-headers step are caught too. Security headers suit a JSON-only API (security review). Tested. |
| new `ClinicQueue.Tests/` | xUnit 2, FluentAssertions 7.x (pinned), `WebApplicationFactory<Program>` in Production with requests from 10.42.0.7, locally signed tokens and a slow test `IClinicSource`; coverlet | Covers the .NET 10 breaks above that can be tested without a database. |

## Found but not changed

- **Bookings: no rate limit, any day from 0001 to 9999, no per-patient cap** (security). The hidden tests
  book days and ids I can't see, so a range rule or limiter could break them. Decide the policy (for example,
  today to today+N, and N bookings a day per `sub`) and add it with `AddRateLimiter` and `TimeProvider`.
- **Queue for an unknown clinic returns an empty snapshot and caches it** (code review). A 404 would change the
  contract, and clinics added after start-up only reach the directory after the next 10-minute refresh.
- **The cache is in memory per pod** (`AddDistributedMemoryCache`). Other pods show a new booking up to a minute
  late. Redis (or HybridCache over Redis) would fix it and needs infrastructure.
- **COUNT + 1 repeats a position after a visit is deleted.** `MAX + 1` or a unique index on
  `(clinic_id, day, position)` would fix it, but that changes the numbering rule or the schema the tests own.
  The DBA should confirm an index on `visit (clinic_id, day[, position])` exists: the model's `HasIndex` creates
  nothing without migrations.
- **Pool size**: the connection string sets no `Maximum Pool Size` (default 100 per pod). Pods × pool must fit
  under PostgreSQL's `max_connections`. It's set in the deployed connection string, not here. Also consider
  `SSL Mode=VerifyFull` there. Npgsql 9+ also defaults `Connection Lifetime` to one hour.
- **Trusted proxy range is the whole pod network (10.42.0.0/16)**: any pod can forge `X-Forwarded-For`.
  It's a production setting the tests rely on, so it stays. A NetworkPolicy allowing only nginx to reach the app
  closes it.
- **Authority is the `staff` realm while `sub` is a patient id** (security). The key stays as it is. Check that
  the realm is the intended one, and consider a policy requiring a patient role or scope.
- **No `UseHttpsRedirection` / `UseHsts` in the app.** Kubelet probes come over plain http from an untrusted node
  IP and would be redirected into a restart loop. Redirect and HSTS at the ingress.
- **`IClinicSource.Load()` is synchronous** (DB I/O on a thread-pool thread, once per 10 minutes). The interface
  is a fixed contract.
- **Layering**: Application references EF/Npgsql (Infrastructure), `ErrorHandler` is ASP.NET Core code in
  Application, `Visit` has no behaviour, and several types share a file. That's a refactor, kept out of the
  upgrade.
- **Exception logs**: a full exception is logged. Npgsql leaves `PostgresException.Detail` out unless
  `Include Error Detail=true`, which isn't set, so row values don't leak. Keep it off.
- **No Dockerfile or pipeline in this folder**: the runtime image tag must move to `10.0` wherever the image is
  built. The default 10.0 images are Ubuntu-based.

## Not proven here

- **Database code never ran: no PostgreSQL or Docker here.** That covers the booking transaction and advisory
  lock, the `DateOnly` mapping on EF/Npgsql 10, `VisitReads`, and the queue query. Line coverage is 52% and branch
  coverage 19%, against targets of 80/70. The uncovered part is almost entirely this database code. Next step:
  Testcontainers PostgreSQL tests, including concurrent bookings.
- **The preflight scan (`upgrade_preflight.py`) did not run**: it needed a permission this session didn't have.
  I worked through its checklist by hand. These applied: `binary-formatter`, `system-linq-async`,
  `forwarded-known-networks`, `forwarded-headers-trust`, `exception-handler-diagnostics`,
  `background-service-start`, `npgsql-date-read`, `npgsql-connection-lifetime`, `image-tag`.
- **No baseline on .NET 8**: only SDK 10 is installed here, and there were no tests before.
