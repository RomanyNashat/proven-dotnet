# Agent Delegation Rules

## Model selection (non-negotiable)

**Any agent that transforms code, evaluates/reviews code, or diagnoses code → Opus.** Opus has the
wider, deeper eyesight; for a harness whose purpose is senior-quality output, the review and
transformation paths are exactly where we do NOT economize — a missed issue or a subtle bad edit
degrades the codebase, and "AI is sloppy" is exactly the reputation this harness exists to defeat.

- **Opus (default):** design, planning, requirements, code transformation (refactors, migrations,
  mapping, scaffolding, TDD implementation), code evaluation/review (reviewers, analysts, auditors,
  coverage, health), diagnosis (build errors), and voice-sensitive writing (stories, handover).
- **Sonnet:** reserved for work that neither transforms nor evaluates code and is low-consequence with
  a downstream check. **Currently no agent qualifies — all run on Opus.** `doc-updater` was the last
  Sonnet agent and moved to Opus once it became clear it edits Swagger attributes inside .cs files and
  verifies that code examples compile: both are code work.

A new agent defaults to Opus. Only drop it to Sonnet if it clearly does not touch code quality or
correctness. When in doubt, Opus.


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
