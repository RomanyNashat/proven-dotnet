---
name: improve
description: "Audit the installed harness (proven-dotnet plus any team layer) against its own rules and propose improvements — enforcement gaps (a rule stated but nothing enforces it), orphan/overlapping skills, coverage gaps (code edits without the parity gate, shareable output without the simplicity layer), stale descriptions. Read-only; proposes fixes one at a time, with approval."
allowed-tools: Read, Bash, Grep, Glob, Agent
disable-model-invocation: true
---

## Improve — the harness audits itself

Uses the `retrospective` skill. Reviews the harness against its own stated rules and finds
where reality has drifted from intent, then walks the fixes one at a time. This is the *proactive*
version of how a harness gets refined: notice the output drifting, fix the rule.

It **complements `tools/check-content.py`** (the mechanical gate in CI) — `/improve` is the *judgment* audit:
the things a script can't check.

### What it checks
- **Enforcement gaps** — a rule in `core.md` / `rules/` / the decision log that nothing actually
  enforces (no reviewer check, no skill applies it). The highest-value finding.
- **Orphan / overlapping skills** — skills referenced by no agent/command (dead budget), or pairs with
  descriptions so similar they'd mis-trigger.
- **Coverage gaps** — a code-editing command not routed through the parity gate; shareable-writing
  output not using the simplicity layer; a side-effect command missing `disable-model-invocation`; a
  code-nav agent not preferring the Roslyn MCP.
- **Description health** — near-ceiling *and* vague descriptions; weak triggers; chronic budget
  pressure (the signal to move service-specific skills into that service repo's `.claude/skills/`, not more trimming).
- **Recurring friction** — when session history is readable: repeated corrections, repeated manual
  steps (command candidates), repeated mis-fires. Skipped honestly when unavailable.

### How it works
```
/improve
  │
  ├── Scan (read-only) → prioritized findings: what drifted + evidence (the rule + the gap) + a fix
  ├── Order by impact  → enforcement & coverage gaps first (the trust risks), then orphans, then health
  └── Walk one at a time → present a finding + proposed fix → you approve / edit / skip → next
```

### Evals
When auditing a generative skill, check whether it has `evals/evals.md` and whether the numbers are
current. A skill with no evals is **unmeasured** — say so rather than guessing at its quality. The
`skill-evals` skill defines what a case and an honest report look like.

### Rules
- **Read-only audit; proposes, never auto-applies.** One finding at a time, approval-gated (plan-first).
- Developer-invoked only (`disable-model-invocation`).
- Every finding needs evidence — the specific rule and the specific place it isn't honored. No padding.
- Complements `tools/check-content.py`; doesn't re-run mechanical checks.
- An applied fix that's a real decision gets a dated entry in the repo's decision log, if it keeps one.
