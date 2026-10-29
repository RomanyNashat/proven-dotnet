# Fixture for the fix eval, round five

The same kind of eval as `fix-sync/`: a run reviews `LabResults` and fixes it, and tests the run never sees
decide, by behavior, whether each finding is fixed and whether what worked still works. No judge.

Rounds three and four couldn't tell the skills from the rules, and round four's task text handed over the
setting (the production base address, "several pods", "patients are in Riyadh"). This task says only
what the hidden tests need kept. Six of the eight findings are what the skills' own tests found in real
libraries, and nothing in the task points at them; two are the rules', as a control.

- `LabResults/`: clinicians record lab results and page through them, look up a patient's lab orders, and
  the patient gets an SMS. PostgreSQL, Keycloak tokens. 8 planted findings. Don't fix it here.
- `HiddenTests/`: one test per finding and six regression tests, run in-process (Production, real
  PostgreSQL, our own JWT key in place of Keycloak, our clock, our log collector, one or two pods).
- `reference/` and `reference-alt/`: two correct fixes that choose differently wherever a fix can (roles
  mapped in the token event or a claims transformation; a (time, id) cursor or an id cursor; HttpClient's
  loggers removed or filtered; any Unicode digit masked or digits made Western first; the queue drained on
  shutdown or an outbox table; the user read on every call or the caller set on each pooled rental).
  Both must pass everything; the original must fail every finding.
- `task.md`: what every run is given, word for word.
- `runs/<arm>-<n>/LabResults/`: the copies the runs fixed, graded by `fix-evals.yml`.

The cases are in `evals.md`, the results in `results.md`, and the per-case counts the eval gate compares
against in `scores.json`.
