# Fixture for the fix eval, round three

The same kind of eval as `fix-hard/`: a run reviews `HomeReadings` and fixes it, and tests the run never
sees decide, by behavior, whether each finding is fixed and whether what worked still works. No judge.
This fixture was written after the skills, so nothing in them was tuned to it.

- `HomeReadings/`: a home blood-glucose service on PostgreSQL, with 13 planted findings. Don't fix it here.
- `HiddenTests/`: one test per finding and six regression tests, run in-process (Production, real
  PostgreSQL, our own JWT key in place of the identity provider, our clock).
- `reference/` and `reference-alt/`: two correct fixes that choose differently wherever a fix can (a
  resilience handler without POST retries or none at all; a cache per patient or no cache; a renamed
  photo or a refused name; `DateTime` in UTC or `DateTimeOffset`; one error middleware or
  `UseExceptionHandler`). Both must pass everything; the original must fail every finding.
- `task.md`: what every run is given, word for word.
- `runs/<arm>-<n>/HomeReadings/`: the copies the runs fixed, graded by `fix-evals.yml`.

The cases are in `evals.md`, the results in `results.md`, and the per-case counts the eval gate
compares against in `scores.json`.
