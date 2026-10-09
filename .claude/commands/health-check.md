---
name: health-check
description: "Grade your codebase's overall health (A–F). Collects coverage, complexity, maintainability, duplication, dead code, analyzer warnings, and security signals into a weighted report card, then shows a prioritized path to the next grade. Read-only — reports, never modifies."
allowed-tools: Read, Bash, Grep, Glob, Agent
---

## Code Health Check

Delegate to the **health-analyst** agent.

Gives you a quick, graded snapshot of where your codebase stands — one letter grade backed by
real metrics, plus a concrete path to level up. Run `/health-check` and follow the prompts.

### The Flow

```
/health-check
  │
  ├── Step 1: Scope
  │   └── Solution-wide, or scoped to specific services?
  │       (scoped reuses the directory-picker: path → folders → multi-select → "another path?")
  │
  ├── Step 2: Collect metrics (automatic)
  │   ├── Test coverage (Coverlet → Cobertura)
  │   ├── Cyclomatic complexity (analyzers)
  │   ├── Maintainability index
  │   ├── Duplication
  │   ├── Dead code
  │   ├── Analyzer warnings (by severity)
  │   ├── Build health
  │   └── Security (vulnerable / deprecated packages)
  │
  ├── Step 3: Score + grade
  │   ├── Each dimension scored 0–100 and graded
  │   ├── Weighted overall (coverage 25%, security 20%, complexity 15%, ...)
  │   └── Overall letter grade A–F
  │
  ├── Step 4: Report card
  │   ├── Overall grade + weighted score
  │   ├── Per-dimension table with notes
  │   └── Biggest drags on the grade (weight × gap)
  │
  ├── Step 5: Path to the next grade
  │   └── Prioritized, highest-leverage moves + which command does each
  │
  └── Step 6: Trend (optional)
      └── Compare to last run, show the delta
```

### How this differs from other commands

| Command | Produces |
|---------|----------|
| `/health-check` | A **grade** — where you stand at a glance |
| `/full-review` | **Findings** — specific issues to fix before merge |
| `/coverage` | One **dimension** (coverage) + the ability to raise it |

Health-check rolls coverage in with several other dimensions into a single graded snapshot.

### Grading at a glance

- Dimensions scored 0–100, then weighted (correctness/safety weighted above polish: coverage 25%,
  security 20%, complexity 15%, maintainability 12%, warnings 10%, duplication 8%, dead code 5%,
  build 5%)
- Weighted score → letter: A (90+), B (80+), C (70+), D (60+), F (<60)
- Coverage thresholds align with the CI gate: 80% line / 70% branch (`rules/testing.md`)
- If a metric can't be measured (tool absent), it's marked N/A and the weights renormalize — no guessing

### Read-only

Health-check computes a grade and a path to improve it. It never modifies code, tests, or
configuration. Each suggested fix points at the command that can act on it (`/coverage` to raise
coverage, `/full-review` for detailed findings, the `refactor-cleaner` agent for cleanup).

### Related

- `/coverage` — raise the coverage dimension specifically
- `/full-review` — detailed pre-merge findings (includes a read-only coverage check)
- `/verify` — full build + test + quality gate before a PR
