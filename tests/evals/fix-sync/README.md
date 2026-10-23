# Fixture for the fix eval, round four

The same kind of eval as `fix-lab/`: a run reviews `MeterSync` and fixes it, and tests the run never sees
decide, by behavior, whether each finding is fixed and whether what worked still works. No judge.

Round three couldn't show the skills add anything over the rules: rules alone also fixed the findings only
the skills teach. So half of this fixture's findings come from what the skills' own tests found in real
libraries, which no rule mentions (a base address that drops its path, a time zone missing on a slim
image, a token's audience left unchecked, CORS that grants any site, two pods sending the same alert).

- `MeterSync/`: pulls home glucose-meter readings from the vendor's cloud and texts the patient when one
  is out of range, on PostgreSQL. 10 planted findings. Don't fix it here.
- `HiddenTests/`: one test per finding and six regression tests, run in-process (Production, real
  PostgreSQL, our own JWT key in place of the identity provider, our clock, one or two pods). The
  production test (M02) runs in its own step with an empty `TZDIR`: an image with ICU but no tzdata.
- `reference/` and `reference-alt/`: two correct fixes that choose differently wherever a fix can (a
  normalised base address or a rebuilt request URI; a scoped directory or a context factory; a claim per
  reading or one pod sweeping under an advisory lock; a time-zone fallback or a fixed offset; the
  invariant culture or ar-SA on the Gregorian calendar; HI/LO without a value or at the meter's limits;
  a mismatched national ID refused or ignored). Both must pass everything; the original must fail every
  finding.
- `task.md`: what every run is given, word for word.
- `runs/<arm>-<n>/MeterSync/`: the copies the runs fixed, graded by `fix-evals.yml`.

The cases are in `evals.md`, the results in `results.md`, and the per-case counts the eval gate compares
against in `scores.json`.
