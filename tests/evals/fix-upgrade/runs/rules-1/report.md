# ClinicQueue: .NET 8 to .NET 10

The service targets `net10.0` and builds with warnings as errors (0 warnings). I added 13 unit tests in
`ClinicQueue.Tests/` and they all pass. Nothing that needs PostgreSQL could run here, so the Npgsql read
in `VisitReads` and the booking insert are checked by compiling only, plus a test of the EF model and
the SQL it generates.

## Changes

| File | What | Why |
|---|---|---|
| `ClinicQueue/ClinicQueue.csproj` | `net8.0` → `net10.0`. JwtBearer 8.0.11 → 10.0.15, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.10 → 10.0.3. | .NET 8 support ended on 10 Nov 2026. These are the latest 10.x patches, and neither has a known vulnerability (direct or transitive). |
| same | Removed `System.Linq.Async`. | .NET 10 has `System.Linq.AsyncEnumerable` built in, so the package's extension methods would clash with it. The one place that used it now uses EF's own `ToArrayAsync`. |
| same | Removed `EnableUnsafeBinaryFormatterSerialization` and `NoWarn SYSLIB0011`. | BinaryFormatter is gone (see `QueueSnapshots`). |
| same | NU1901–NU1904 are now errors (they used to be listed in `WarningsNotAsErrors`). Added `NuGetAuditMode=all` and made CS8600/CS8602/CS8603 errors. | Any known-vulnerable package, direct or transitive, now fails the build instead of passing quietly. |
| same | `InternalsVisibleTo ClinicQueue.Tests`. | The tests use the snapshot serializer and cache key, which are `internal`. |
| `ClinicQueue/Program.cs` | `KnownNetworks.Add(new HttpOverrides.IPNetwork(...))` → `KnownIPNetworks.Add(System.Net.IPNetwork.Parse(...))`. If `Proxies:Network` is missing, start-up fails with a clear message. | Both old APIs are obsolete in .NET 10 (ASPDEPR005), which breaks the build with warnings as errors. Behaviour is the same: requests from 10.42.0.0/16 are trusted, `ForwardLimit = 2`, and `/whoami` returns the client IP from `X-Forwarded-For` (tested with the request coming from 10.42.0.7). |
| same | `ExceptionHandlerOptions.SuppressDiagnosticsCallback = _ => false`. | In .NET 10 the exception middleware no longer logs an exception once an `IExceptionHandler` has handled it. `ErrorHandler` handles every exception, so on .NET 10 no 500 would have been logged at all. This puts the .NET 8 logging back. A test confirmed the log entry goes missing without this line. |
| `ClinicQueue/Application/DirectoryWarmer.cs` | The first `Refresh()` moved from `ExecuteAsync` into `StartAsync`. | In .NET 10, `BackgroundService` runs all of `ExecuteAsync` in the background, including the synchronous part before the first `await`. The service could then take requests before the clinic list was loaded, so `GET /clinics` might return `[]`. Now the host waits for the first load again, and a failed first load stops start-up, as it did on .NET 8. A test showed the bug, then the fix. |
| same | A failure in the 10-minute refresh is now logged and the previous list is kept. Before, the exception ended `ExecuteAsync`. | With the default `BackgroundServiceExceptionBehavior.StopHost`, a short database outage would have stopped the whole pod. Cancellation still ends the loop. |
| `ClinicQueue/Application/QueueSnapshots.cs` | Snapshots are cached as `System.Text.Json` instead of `BinaryFormatter`. The DB query now selects `Position` and builds the array in the database with EF `ToArrayAsync`, instead of `AsAsyncEnumerable()` + System.Linq.Async. The cache key is built with the invariant culture. | BinaryFormatter was removed in .NET 9: on .NET 10 every `GET /clinics/{id}/queue` would have thrown. It was also a remote code execution risk. The cache is in-process memory (`AddDistributedMemoryCache`), so no entries in the old format survive a deploy and no migration is needed. The cache key used the current culture to format the date, and some cultures (`ar-SA`) change the digits and calendar. |
| `ClinicQueue/Application/QueueSnapshot.cs` (new) | `QueueSnapshot` is now a sealed record `(ClinicId, DateOnly Day, int[] Positions)` in its own file. | It no longer needs `[Serializable]` or mutable setters for BinaryFormatter. One class per file. |
| `ClinicQueue/Domain/Visit.cs` | `Day` is `DateOnly` (it was `DateTime`). | The column is PostgreSQL `date`. `DateOnly` is what Npgsql maps it to directly, with no `Kind` or time-of-day to strip. The table and columns are the same, and a test checks the model (`visit`: `id`, `clinic_id`, `patient_id`, `day` `date`, `position`). |
| `ClinicQueue/Application/VisitReads.cs` | Uses a reader with `GetFieldValueAsync<DateOnly>` instead of `(DateTime)ExecuteScalarAsync(...)`. Returns `DateOnly?`. | The CLR type that `ExecuteScalar` returns for a `date` column has changed across Npgsql major versions. Asking for the type explicitly avoids an `InvalidCastException` on `GET /visits/{id}/day`. The owner filter (`patient_id = @patient`) is unchanged. |
| `ClinicQueue/Api/QueueEndpoints.cs` | `TryParseDay` uses `DateOnly.TryParseExact` (invariant culture, `yyyy-MM-dd`). | Follows the `DateOnly` model. Routes, request and response shapes, and status codes are unchanged. |
| `ClinicQueue.Tests/` (new) | xUnit 2, FluentAssertions pinned to `[7.0.0,8.0.0)`, `WebApplicationFactory<Program>` in Production, with the request coming from 10.42.0.7 and a self-signed JWT. | Covers: forwarded client IP, the clinic list being ready at start-up, 401 without a token, anonymous `/health/live`, 400 for a bad `day`, 500 ProblemDetails plus a log entry, warmer start-up order, snapshot round trip and cache hit, culture-proof cache key, and EF model/SQL mapping. |

## Found, not changed

- **`POST /visits` can give two patients the same position.** It runs `COUNT + 1` and then inserts, with no lock or unique constraint, so two bookings at the same moment can both get the same number. The fix needs a unique index on `(clinic_id, day, position)` with a retry, or a per-clinic-day lock. That changes the database, which the tests own, so I left it.
- **`IClinicSource.Load()` is synchronous**, and so is `EfClinicSource` (`ToList()`). It is part of the contract the tests replace, so it stays. It only runs at start-up and every 10 minutes, off the request path.
- **`GET /clinics/{id}/queue` has no per-patient check.** It returns position numbers only, with no patient data, for any authenticated caller. I assumed waiting-room screens need this. Worth confirming that is intended.
- **A non-numeric `sub` claim returns 500**: `int.Parse` in `PatientId`. The identity provider is trusted to issue numeric ids. Kept as is.
- **No HTTPS redirection or HSTS.** TLS ends at the ingress, and adding redirection would break the plain-HTTP traffic from nginx.
- **`VisitReads` is a singleton that creates an `NpgsqlConnection` per call** from `IConfiguration`. Pooling covers it. Switching to a registered `NpgsqlDataSource` would read the connection string when services are registered, before a test host's overrides apply.
- **No input-validation framework** (FluentValidation or .NET 10 `AddValidation`). The endpoints already reject a bad `day` and an unknown clinic with 400, and I kept the response shapes exactly.
- **`DirectoryWarmer` uses `PeriodicTimer` without `TimeProvider`.** No behaviour depends on it.
- **There is no Dockerfile or pipeline file in the folder.** If the image is built elsewhere, its base image has to move to the .NET 10 `aspnet` image. Since .NET 10 that image is Ubuntu-based rather than Debian-based.
- **`appsettings.json` holds a connection string without a password.** Presumably the password is supplied by the environment or Vault in the cluster. There is no secret in the repo.
