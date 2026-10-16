# Fixture for the fix eval

The review evals count what a review finds. This one checks what a fix does: a run reviews
`ClinicReminders` and fixes it, and tests the run never sees decide, by behavior, whether each finding is
fixed and whether anything that worked still works. No judge.

- `ClinicReminders/`: the service, with 14 planted findings (the same ones as `full-review-hard`, made to
  build and run). Don't fix it here.
- `HiddenTests/`: one test per finding and five regression tests. They run the service in-process
  (Production, real SQL Server). `H03` runs with no time-zone database; `H04` on an ICU whose `ar-SA` is
  Hijri; `H11` is a NuGet audit of the restore.
- `reference/`: one correct fix, which must pass everything. The original must fail every finding test.
- `runs/<name>/ClinicReminders/`: the copies a run fixed, graded by `fix-evals.yml`.

The cases, the contract the runs get, and the scoring are in `evals.md`; results in `results.md`.
