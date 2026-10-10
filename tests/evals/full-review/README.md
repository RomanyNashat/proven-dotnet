# Fixture for the `/full-review` and `/health-check` evals

`PharmacyRefills` is a small made-up service with planted findings, for the cases in `evals.md` (here)
and `.claude/skills/code-health/evals/evals.md`. It's an input for subagent runs, not code that builds in
CI: nothing restores it. Not installed: the installer copies only `.claude/`.

The planted findings and the distractors are listed in `evals.md`. Don't fix them: a case passes when
the review finds them.
