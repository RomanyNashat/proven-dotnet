# The upgrade eval: results

## 2026-11-14: round one, the harness against the rules alone, three runs each

Each run moved its own copy of `ClinicQueue` from .NET 8 to .NET 10 in a clean home, given `task.md` word
for word, with `claude-opus-5-5` at medium effort (`tools/run-queue.sh`). Both arms were public `main` at
0.3.19. The with-proven arm also got `route.txt`: the `dotnet-upgrade` skill starting with its preflight
scan, then `/full-review`. The rules-only arm had `core.md` and the rule files, nothing else. The copies
are in `runs/` as the runs left them, with their reports. `fix-evals.yml` grades them.

| | With the harness | The rules alone |
|---|---|---|
| **Findings fixed** (3 runs × 7) | **21 / 21** (7, 7, 7) | **21 / 21** (7, 7, 7) |
| Regressions kept (3 runs × 6) | 18 / 18 | 18 / 18 |
| Builds | 3 / 3 | 3 / 3 |
| Test methods added (the original has none) | 13, 15, 17 | 12, 10, 6 |
| Cost per run | $3.85, $3.76, $4.39 | $1.79, $1.68, $1.16 |
| Time per run | 9, 9, 12 min | 7, 8, 4 min |

The controls graded as before: the original 0/7, `compile-only` 3/7 (U02, U03, U04 and U06 still fail),
both reference fixes 7/7.

## What it means

- **This round doesn't tell the arms apart.** Every run fixed all seven, the four that only show at run
  time included. The model already knows these .NET 10 breaks: the rules-only runs named each one in their
  reports (the exception handler that stops logging, `ExecuteAsync` running in the background, `date` as
  `DateOnly`, `KnownIPNetworks`) without the skill. One of them fixed U02 another way:
  `SuppressDiagnosticsCallback = _ => false` keeps the middleware's own log, and the test passes.
- **The harness cost 2.5 times as much** ($4.00 a run against $1.54) and bought what `/full-review` adds.
  All three with-proven runs fixed the booking race (two bookings at once got the same position) under a
  transaction and an advisory lock. All three rules-only runs saw it too, and listed it as needing a schema
  change, out of scope. The with-proven runs also kept the directory when a 10-minute refresh failed,
  where one rules-only run did; two of them added security headers. Nothing here grades those.
- **So the skill's value for an upgrade, if any, is in the breaks the model doesn't know.** The seven here
  are the well-known ones. A second round is worth running only with the checks this round didn't plant:
  empty environment variables binding to null, `HttpClientFactory` header redaction, Npgsql `Connection
  Lifetime`, EF Core's `Application Name` on SQL Server, JSON on Azure SQL. Without that, the measured
  answer is that the rules plus the model are enough for these seven, and the route is the dearer way.

## Disclosures

- **The preflight scan was refused before the change, in every with-proven run.** The runner's tool list
  allows `dotnet` and file commands, not `python3`, and the skill doesn't grant it (the `/full-review`
  command does, so the known-traps scan ran once the review started). Each run went through the skill's
  table of checks by hand instead; proven-3 ran the scan once afterwards, on its finished copy. So the
  skill was read, and its script wasn't tested as the first step. The runner is fixed in #85. A developer
  in an interactive session is asked to approve the script and isn't stopped.
- **The scan would have missed U04 anyway**, as the README says: the cast is on a later line than the
  untyped read. Left as is for this round; widened in #85.
- **One round, three runs per arm, one model and effort.** With both arms at the ceiling, more runs of
  these seven findings wouldn't change the conclusion.
