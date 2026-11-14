# The upgrade eval: moving a .NET 8 service to 10

The other fix evals ask a run to review a service and fix it. This one asks it to move `ClinicQueue` from
.NET 8 to 10, which lost support for 8 on 10 November 2026, and keep it working. Hidden tests decide, on
.NET 10 and against PostgreSQL, whether each break the move brings was handled and whether what worked
still works. No judge.

- `ClinicQueue/`: patients book clinic visits; a worker keeps the clinic list in memory; the waiting-room
  screens poll a cached queue; it runs behind nginx. On .NET 8, with warnings as errors. Don't fix it here.
- `HiddenTests/`: one test per finding and six regression tests, on .NET 10, in-process (Production, real
  PostgreSQL, our own JWT key, every request from nginx's pod). Each finding test first requires the copy
  to be built for .NET 10, so the original fails them all.
- `reference/` and `reference-alt/`: two correct moves that choose differently where a move can (`DateTime`
  read by name or `DateOnly` end to end; the first fill in the worker's `StartAsync` or its own hosted
  service; the error logged in the handler or the handler returning `false`; JSON or text in the cache).
  Both must pass everything.
- `compile-only/`: the move that only fixes what stops the build (framework, packages, `KnownIPNetworks`,
  `System.Linq.Async` removed). It builds, passes U01, U05 and U07, and fails U02, U03, U04 and U06: those
  four breaks only show when the service runs on 10. That is what this eval measures.
- `task.md`: what every run is given, word for word; `route.txt`: the harness's route for the with-proven
  arm (`run-round.sh` adds it).

## The findings

| Test | The break on .NET 10 | Passes when | Where it's taught |
|---|---|---|---|
| U01 | the framework and packages | the service is built for .NET 10, with ASP.NET Core, EF Core and Npgsql on 10 | `dotnet-upgrade` |
| U02 | an `IExceptionHandler` that returns `true` leaves no error in the logs | a failed request is logged with its exception | `dotnet-upgrade` (tested section) |
| U03 | a worker's set-up before its first `await` no longer runs before the host has started | the clinic list is there on the first request, even when it loads slowly | `dotnet-upgrade` (tested section) |
| U04 | Npgsql 10 returns `date` as `DateOnly` | a visit's day reads, on Npgsql 10 | `dotnet-upgrade` (tested section) |
| U05 | `KnownNetworks` is obsolete, an error under warnings as errors | behind nginx the service still sees the client's address | `dotnet-upgrade`, `nginx` |
| U06 | `BinaryFormatter` always throws | the queue is served, and served again from the cache | `dotnet-upgrade` (scan) |
| U07 | `System.Linq.Async` makes async LINQ ambiguous | the package is gone | `dotnet-upgrade` |

Regressions, which pass on the original and must still pass: R1 a patient books a visit and gets the next
position; R2 another patient's visit is not found; R3 the API needs a token for `clinic-queue`; R4 health
is open; R5 the clinic list comes from the database; R6 a day that isn't `yyyy-MM-dd` is a 400.

`fix-evals.yml` fails if the original passes any finding test or either reference fails any test.

The harness's preflight scan (`upgrade_preflight.py`, 0.3.19) was run on the original once, after the
fixture was written: it points at U02, U03, U05, U06 and U07, and misses U04, because its
`npgsql-date-read` pattern doesn't see a cast of a value read on an earlier line. The scan is left as it
is for the first round so the runs measure the harness as it shipped; widening that pattern comes after.

Round one (2026-11-14) is in `results.md`: both arms fixed 21/21, so these seven findings don't tell the
arms apart.
