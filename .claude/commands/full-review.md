---
name: full-review
description: Full code review covering quality, security, and database patterns. Runs code-reviewer, security-reviewer, and dba-reviewer in parallel, plus read-only test coverage and dependency vulnerability (direct + transitive) checks. Use after implementation is complete.
allowed-tools: Read, Grep, Glob, Bash, Agent
---

Run three review agents in parallel:

1. **code-reviewer** agent (Read-only, Opus)
   - C# idioms, SOLID, naming, performance, maintainability
   - Check against `rules/csharp-standards.md` and `rules/coding-style.md`

2. **security-reviewer** agent (Read-only, Opus)
   - OWASP Top 10:2025, auth, PII exposure, cryptographic correctness
   - Reference `skills/owasp-aspnetcore/` for specific mitigations
   - Check against `rules/security.md`

3. **dba-reviewer** agent (Read-only, Opus)
   - N+1 detection, migration safety, index strategy, connection management
   - Reference `skills/efcore-patterns/`, `skills/dapper-patterns/`
   - Check against `rules/efcore-rules.md`

Also run a **read-only test coverage check** (no test writing, no raising):
- Reuse the measurement logic in `skills/test-coverage/` §1 and §5 ONLY
- Report line coverage % (vs 80% target) and branch coverage % (vs 70% target)
- List the least-covered classes (risk-ranked)
- Mark below-threshold values with ⚠️; if both thresholds are met, a single ✓ line is enough
- Point to `/coverage` as the next step — `/full-review` never raises coverage itself

Also run a **read-only dependency vulnerability check** (report only, no upgrades):
```bash
dotnet list package --vulnerable --include-transitive
dotnet list package --deprecated
```
- Report any vulnerable packages — **both direct and transitive** — with the package name, the
  resolved version, severity, and the advisory (e.g. GHSA/CVE) where shown
- Clearly mark whether each hit is a direct dependency or transitive (pulled in by another package)
- Flag deprecated packages separately as a lower-severity note
- Map severity to the report levels below: high/critical vulnerability → **Critical**;
  moderate → **Warning**; low/deprecated → **Suggestion**
- This is informational — `/full-review` reports the findings but never changes package versions.
  Point to `/security-scan` for the full audit and to a manual `dotnet add package` upgrade as the fix.

Consolidate findings into a single report with severity levels:
- **Critical** (must fix before merge)
- **Warning** (should fix)
- **Suggestion** (nice to have)
- **Positive** (good patterns to acknowledge)

End the report with two read-only informational sections:
- **Test Coverage** — line %, branch %, least-covered classes, and a pointer to `/coverage`.
- **Dependency Vulnerabilities** — vulnerable packages (direct and transitive) with severity and
  advisory, plus any deprecated packages, and a pointer to `/security-scan`.

Both sections are informational — they report, they never modify tests, coverage, or package versions.

### After the review
Findings that are pure cleanup — dead code, needless complexity, outdated syntax — go to the
**`refactor-cleaner`** agent. It is behaviour-preserving work, so it runs under the parity gate
(characterization test first where none exists) and never lands unreviewed.
