---
name: code-health
description: Codebase health for .NET: weighted A–F report card from quality metrics (coverage, complexity, duplication, dead code, warnings). Used by health-analyst + /health-check.
version: 1.1.0
---

# Code Health Skill

This skill produces a graded **report card** for a .NET codebase — a quick A–F snapshot of
overall quality with the specifics behind the grade. It powers the `/health-check` command via
the health-analyst agent.

It is deliberately different from `/full-review`: full-review produces *findings* (issues to
fix); health-check produces a *grade* (where you stand at a glance). They complement each other.

It also differs from `/coverage`: coverage is one dimension; health-check rolls coverage in with
several others into a single grade.

---

## 1. Metrics Collected

The grade is computed from these dimensions. Each is gathered from common tooling (Coverlet,
Roslyn analyzers — Roslynator / Meziantou / .NET analyzers — and `dotnet build`).

| Dimension | What it measures | How to gather |
|-----------|-----------------|---------------|
| **Test coverage** | Line + branch coverage | Reuse `skills/test-coverage/` §1 (Coverlet → Cobertura) |
| **Cyclomatic complexity** | Method/branch complexity | CA1502 (`dotnet_diagnostic.CA1502.severity = warning` for the run) / Roslynator; flag methods over threshold |
| **Maintainability index** | Composite 0–100 (Microsoft metric: complexity + lines + Halstead volume) | `dotnet build` with code metrics, or analyzer output |
| **Duplication** | % duplicated blocks | Only if a duplication tool already runs (in CI or locally); otherwise N/A — grade on the other dimensions and say so |
| **Dead code** | Unused members/types | Analyzer warnings (IDE0051/0052, RCS unused), unreferenced symbols |
| **Analyzer warnings** | Count + severity of analyzer hits | `dotnet build` warning count by severity |
| **Build health** | Clean build, no warnings-as-errors failures | `dotnet build` exit + warning count |
| **Security signals** | Vulnerable packages, obvious flags | `dotnet list package --vulnerable`; reference `skills/owasp-aspnetcore/` |

### 1.1 Gathering Commands

```bash
# Coverage (see test-coverage skill for parsing)
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Build with warnings surfaced (count by severity)
dotnet build -warnaserror- /clp:Summary 2>&1 | tee ./coverage/build.log

# Vulnerable + deprecated packages
dotnet list package --vulnerable --include-transitive 2>&1
dotnet list package --deprecated 2>&1

# Code metrics (if Microsoft.CodeAnalysis.Metrics installed) or rely on analyzer output
```

Where a metric can't be gathered (tool not present), mark that dimension **N/A** and exclude it
from the weighting (renormalize the remaining weights) rather than guessing. Note the exclusion
in the report.

**Security is the exception: it isn't dropped when the package scan can't run** (no feed, no network).
Read the direct `PackageReference`s and check each against a published advisory (GitHub Advisory
Database, the NuGet vulnerability feed). A confirmed match counts in the Security score like a tool hit,
with the advisory named as the source. Say the transitive tree and deprecations weren't checked. A
vulnerable package left out of the grade because "the tool didn't confirm it" is a miss: the grade then
says the service is safer than it is.

> **With `proven-roslyn` connected,** dead code (`find_dead_code`) and build diagnostics
> (`get_diagnostics`) come straight from it, far more cheaply than parsing build output. The grading
> logic stays identical — only the gathering source changes.

---

## 2. Scoring Rubric

### 2.1 Per-Dimension Score (0–100)

Each dimension is scored 0–100, then graded:

| Dimension | 100 (A) | 80 (B) | 60 (C) | 40 (D) | <40 (F) |
|-----------|---------|--------|--------|--------|---------|
| Line coverage | ≥90% | ≥80% | ≥65% | ≥50% | <50% |
| Branch coverage | ≥85% | ≥70% | ≥55% | ≥40% | <40% |
| Avg complexity / method | ≤5 | ≤8 | ≤12 | ≤18 | >18 |
| Maintainability index | ≥85 | ≥70 | ≥55 | ≥40 | <40 |
| Duplication | ≤3% | ≤5% | ≤10% | ≤15% | >15% |
| Dead code | none | ≤5 items | ≤15 | ≤30 | >30 |
| Analyzer warnings | 0 | ≤10 | ≤30 | ≤75 | >75 |
| Build health | clean | clean | warnings | warnings | broken |
| Security | 0 vulnerable | 0 vulnerable | low only | moderate | high/critical |

The 80%/70% coverage rows align with the CI thresholds in `rules/testing.md`.

### 2.2 Weights

The overall score is a weighted average. Default weights (tuned so correctness/safety outweigh
polish):

| Dimension | Weight |
|-----------|--------|
| Test coverage (line+branch averaged) | 25% |
| Security | 20% |
| Cyclomatic complexity | 15% |
| Maintainability index | 12% |
| Analyzer warnings | 10% |
| Duplication | 8% |
| Dead code | 5% |
| Build health | 5% |

Weights sum to 100%. If a dimension is N/A, drop it and renormalize the rest proportionally.

### 2.3 Overall Grade

Map the weighted score to a letter:

| Score | Grade |
|-------|-------|
| 90–100 | A |
| 80–89 | B |
| 70–79 | C |
| 60–69 | D |
| <60 | F |

Use +/- within bands if useful (e.g., 88 → B+, 81 → B-). Keep it simple; the letter is the headline.

---

## 3. The Report Card

Present a clear, scannable report card:

```
Code Health Report Card — [Service or Solution]
══════════════════════════════════════════════
Overall Grade:  B-   (weighted score 81/100)

Dimension              Score  Grade   Notes
──────────────────────────────────────────────
Test coverage          78     C+      line 78% / branch 61% (below 80/70 target)
Security               100    A       no vulnerable packages
Cyclomatic complexity  72     C       4 methods over threshold (worst: 23)
Maintainability        85     B       solid
Analyzer warnings      90     A-      7 warnings
Duplication            95     A       2.1%
Dead code              80     B       6 unused members
Build health           100    A       clean build
──────────────────────────────────────────────

Biggest drags on the grade:
  1. Test coverage (C+, 25% weight) — raising to 80/70 lifts overall to ~B+
  2. Complexity (C, 15% weight) — NotificationConsumer.ConsumeAsync is 23
```

### 3.1 Path to the Next Grade

Always end with a concrete, prioritized "how to level up" — the highest-leverage moves
(weight × gap) to reach the next letter:

```
Path to A-:
  • Raise coverage to 80% line / 70% branch   → run /coverage   (+~5 overall)
  • Reduce complexity in ConsumeAsync + 3 others (extract methods)   (+~3 overall)
  • Clear 7 analyzer warnings   (+~1 overall)
  Projected: B- (81) → A- (90)
```

This mirrors the `/coverage` raise menu philosophy — show the target and the route, don't just
state the grade. Health-check itself is **read-only and reporting**: it computes the path but
does not implement it. Point each item at the right command (`/coverage`, the `refactor-cleaner`
agent for cleanup, `/full-review` for detail).

---

## 4. Scope

Health-check can run on the whole solution or be scoped to specific services (reusing the
handover directory-picker, `skills/handover/` §1.5.1):

- **Solution-wide:** one overall grade for everything
- **Per-service:** grade each selected service separately, then an overall solution grade

Per-service grading helps target the weakest service rather than averaging its problems away.

---

## 5. Trend Tracking (lightweight)

Optionally store each run's result so the developer can see movement over time:

- Append a one-line record to `./coverage/health-history.json` (gitignored, like other coverage
  artifacts): `{ "date": "...", "scope": "...", "grade": "B-", "score": 81 }`
- On the next run, show the delta: "B- (81) — up from C+ (76) last month"
- If no history exists, this is the first data point — just note that.

Never commit the history file. Keep it alongside the coverage artifacts.

---

## 6. Output Handling

- Present the report card inline by default
- If the developer wants it saved, write `health-report.md` to the project root
- Health-check never modifies code, tests, or configuration — it reports and points to the
  commands that can act (`/coverage`, `/full-review`, etc.)
