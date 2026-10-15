# Harder fixture for the `/full-review` evals

`ClinicReminders` is a small made-up service whose planted findings are the ones a careful reviewer
without the skills tends to miss: each was found by a skill's own test in CI. It's an input for subagent
runs, not code that builds in CI. The cases and the scoring are in `evals.md`.

Don't fix the findings: a case passes when the review finds them.
