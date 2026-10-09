# Fixture for the `/patterns` command evals

`ClinicBooking` is a small made-up service for the set C cases in
`.claude/skills/design-patterns/evals/evals.md`. It is an input for subagent runs, not code that builds:
nothing is restored and CI doesn't compile it. It carries nine planted findings, listed with their
expected verdicts in the evals file, including one distractor (a class named `RetryStrategy` that isn't a
strategy). Not installed: the installer copies only `.claude/`.
