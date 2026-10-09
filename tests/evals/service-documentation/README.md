# Fixtures for the `service-documentation` evals

Small, made-up services that the cases in `.claude/skills/service-documentation/evals/evals.md` point
at. They are inputs for subagent runs, not code that builds: no project references are restored and CI
doesn't compile them. Each one carries the trap its case needs (a missing endpoint, a real gotcha, an
inaccurate README). Not installed: the installer copies only `.claude/`.
