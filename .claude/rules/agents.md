# Agent Delegation Rules

## Model selection (non-negotiable)

**Any agent that transforms, reviews or diagnoses code runs on Opus**: a missed issue or a subtle bad
edit is the cost this harness exists to avoid. That covers design, planning, refactors, migrations, TDD,
every reviewer and analyst, build errors, and voice-sensitive writing. Sonnet only for work that neither
changes nor judges code and has a check after it; today no agent qualifies. A new agent defaults to Opus.


## The Standard Pipeline
For a new feature, follow this flow:
```
User request
  → planner (decompose into tasks with acceptance criteria)
  → architect (if design decisions needed: service boundaries, DB schema, ADRs)
  → tdd-guide (write tests first, then implement)
  → [parallel review]:
      code-reviewer (quality, patterns, C# idioms)
      security-reviewer (OWASP, auth, PII, crypto)
      dba-reviewer (queries, indexes, migrations, N+1)
  → e2e-runner (integration tests with real dependencies)
  → doc-updater (README, XML docs, ADR updates)
```

## Rules
- Reviewers (code-reviewer, security-reviewer, dba-reviewer) are READ-ONLY — they report findings, they don't modify code.
- The planner orchestrates — it decides which agents to involve and in what order.
- For small changes (< 50 lines, single file), review inline without delegating.
- For bug fixes, always start with tdd-guide to write a reproducing test first.
- For production incidents (slow, leaking, restarting, starving), gather evidence with the `production-diagnostics`
  skill first (logs, APM traces, safe requests to the platform team), then tdd-guide for the fix. build-error-resolver is for
  build, test and pipeline failures, or a single exception with its stack trace.
- compliance-auditor is opt-in — only invoke for healthcare or regulated industry features.
- Parallel reviews (code + security + DBA) are independent and can run simultaneously.
