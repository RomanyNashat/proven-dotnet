# Fixture for the skills-only `/full-review` eval

`Appointments` is a small made-up service whose planted findings are all things the always-loaded rules
don't mention: each comes from a skill and its tests. It's an input for review runs, not code that builds
in CI (there's no proto for its gRPC types). The cases and the scoring are in `evals.md`; results in
`results.md`.

Don't fix the findings: a case passes when the review finds them.
