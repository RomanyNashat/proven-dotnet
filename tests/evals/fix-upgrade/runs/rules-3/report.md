# ClinicQueue: .NET 8 → .NET 10

The service builds on `net10.0` with warnings as errors and no NuGet audit findings. The 6 tests in `ClinicQueue.Tests/` pass. Nothing that needs PostgreSQL ran here.

## Changes

| File | What | Why |
|---|---|---|
| `ClinicQueue/ClinicQueue.csproj` | `net8.0` → `net10.0`. JwtBearer 8.0.11 → 10.0.15, Npgsql EF 8.0.10 → 10.0.3. | .NET 8 is out of support. |
| same | Removed `System.Linq.Async`. | .NET 10 has `System.Linq.AsyncEnumerable` built in. Keeping the package makes the async LINQ calls ambiguous. |
| same | Removed `EnableUnsafeBinaryFormatterSerialization` and the `SYSLIB0011` suppression. | BinaryFormatter is gone (see `QueueSnapshots`). |
| same | Removed `WarningsNotAsErrors NU1901–NU1904`. Set `NuGetAuditMode=all`, `NuGetAuditLevel=low`, NU1901–1904 and CS8600/8602/8603 as errors. | Known-vulnerable packages (direct or transitive) were being let through quietly. Now they fail the build. Nothing is flagged today. |
| `ClinicQueue/Application/QueueSnapshots.cs` | Replaced BinaryFormatter with `System.Text.Json`. `QueueSnapshot` is now a record with `DateOnly Day`. The query projects `Position` in SQL (`Select` before `ToArrayAsync`) instead of going through `AsAsyncEnumerable`. The cache key is built with the invariant culture. | **On .NET 9 and later, BinaryFormatter always throws**, so every `GET /clinics/{id}/queue` would have returned 500. It was also a deserialization risk. The cache is in-process memory, so no old entries need to be read. The culture fix keeps the key from depending on the pod's culture (e.g. a Hijri calendar). |
| `ClinicQueue/Application/DirectoryWarmer.cs` | Moved the first `Refresh()` from `ExecuteAsync` into `StartAsync`, before `base.StartAsync`. | **In .NET 10 the host runs all of `ExecuteAsync` in the background**, including the part before the first `await`. Start-up no longer waits for the first load, so early `GET /clinics` calls could return `[]`. The server only starts after `StartAsync` returns, so the list is full before any request. |
| `ClinicQueue/Program.cs` | `KnownNetworks.Add(new HttpOverrides.IPNetwork(...))` → `KnownIPNetworks.Add(System.Net.IPNetwork.Parse(...))`. A missing `Proxies:Network` now gives a clear error. Removed the unused `using System.Net`. | The old API is obsolete in .NET 10 (ASPDEPR005), which breaks the build when warnings are errors. The trusted network (`10.42.0.0/16`), `ForwardLimit = 2` and the X-Forwarded-For/Proto handling are unchanged. |
| `ClinicQueue/Domain/Visit.cs`, `Api/QueueEndpoints.cs` | `Visit.Day` and the parsed day are now `DateOnly` (`DateOnly.TryParseExact`, invariant, `yyyy-MM-dd`). | This is the natural type for the `date` column and matches Npgsql 10's default. The column, the mapping (`HasColumnType("date")`) and the JSON strings are unchanged. |
| `ClinicQueue/Application/VisitReads.cs` | Reads `day` with `GetFieldValueAsync<DateOnly>` and returns `DateOnly?`. | **Npgsql 10 returns `date` as `DateOnly`**, so the old `(DateTime)ExecuteScalarAsync(...)` would throw `InvalidCastException` on every `GET /visits/{id}/day`, which means a 500. |
| `ClinicQueue/Application/ErrorHandler.cs` | Logs the exception itself (source-generated `LoggerMessage`: method, path, trace id). | **In .NET 10 the exception handler middleware no longer logs exceptions that an `IExceptionHandler` reports as handled**, so production would have stopped logging unhandled errors. The response (a 500 ProblemDetails with no detail) is unchanged. |
| `ClinicQueue.Tests/` (new) | xUnit 2 with FluentAssertions `[7.0.0,8.0.0)`. Tests: the snapshot JSON round-trip; the warmer fills the directory before `StartAsync` returns; and a `WebApplicationFactory<Program>` setup in Production with requests from 10.42.0.7 and self-signed JWTs, checking `/whoami` behind ingress + nginx, `/clinics` on the first request, 401 without a token, and anonymous `/health/live`. | These cover the behaviours that changed. Note: on .NET 10 the old warmer could still pass the `/clinics` test by luck, because the fake source is instant. The `StartAsync` unit test is the one that pins the fix. |

Routes, request/response shapes, the `clinic`/`visit` tables and columns, `IClinicSource`/`ClinicEntry`, the JWT setup (default scheme, `Auth:Authority`, audience `clinic-queue`, `MapInboundClaims = false` so `sub` stays) and the settings are all unchanged.

## Found but not changed

- **Positions can be duplicated.** `POST /visits` counts existing visits and adds one with no lock or unique constraint, so two patients booking at once can get the same position. The fix needs a schema change (a unique `(clinic_id, day, position)` index), and the tests own the schema.
- **A failed 10-minute refresh stops the host.** If `IClinicSource.Load()` throws during a refresh (e.g. a database blip), the exception escapes `ExecuteAsync` and, with the default `BackgroundServiceExceptionBehavior.StopHost`, the pod stops. This was the same on .NET 8. Suggested fix: log and keep the last list.
- **`IClinicSource.Load()` is synchronous I/O.** The interface is fixed by the tests.
- **`VisitReads` builds its own `NpgsqlConnection`** from configuration. It works and is pooled, but a shared `NpgsqlDataSource` would be cleaner.
- **JWT `ClockSkew` is the default 5 minutes** (our standard is 30 s). I left token acceptance as production has it during the upgrade. It's worth a separate change.
- **No HSTS, HTTPS redirect or security headers.** TLS ends at the ingress, so I didn't add any. Adding them in-process could break the cluster set-up and the tests.
- **No validation layer on `BookVisit`.** The endpoint checks the day format and that the clinic exists, which covers the inputs.
- **`GET /clinics/{id}/queue` has no per-clinic ownership check.** It returns only position numbers, with no patient data, to any signed-in user. That looks intended for the waiting-room screens. `/visits/{id}/day` does check the patient.
- `UseForwardedHeaders` runs before `UseExceptionHandler`. That is the right order for forwarded headers, so I kept it.
