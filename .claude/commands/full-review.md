---
name: full-review
description: Full code review covering quality, security, and database patterns. Runs code-reviewer, security-reviewer, and dba-reviewer in parallel, plus read-only test coverage and dependency vulnerability (direct + transitive) checks. Use after implementation is complete. With --diff, reviews a change (a branch, a merge request) instead of the whole service.
argument-hint: "[--diff [base branch]]"
allowed-tools: Read, Grep, Glob, Bash, Agent
---

## Reviewing a change: `--diff`

Without arguments this reviews the whole service. With `--diff` (or when the developer asks to review a
branch, a merge request or "my changes"), it reviews **the change**, and everything below runs in that
mode:

1. **Get the change, read-only.** The base is the branch the change merges into (`origin/main` unless the
   developer names another). This form includes uncommitted work, and works in PowerShell and bash:
   ```
   git diff -U0 $(git merge-base origin/main HEAD) --output=change.diff
   ```
2. **The scan takes the diff:** add `--diff change.diff`. It marks each finding `changed` or `touched` and
   only counts the rest.
3. **Each reviewer gets the diff and the list of changed files.** It reads the changed files whole, and
   whatever the change calls or is called by (`find_callers` for a changed signature).
4. **A finding needs the change to take part in it.** Report a flaw only when the change
   - **wrote it:** the flawed line is one the change adds or edits;
   - **reaches it:** the change makes an existing flaw reachable or worse (a new caller of an unsafe
     method, a guard it removed, a new endpoint over old code that skips the ownership check); or
   - **breaks something:** a caller of a signature or a behavior the change altered.
   Each finding, Suggestions too, says which of the three. A flaw the change neither wrote nor reaches is
   not a finding here, however bad: it goes in the count below. When the fix for a change finding would
   also close an old flaw, say so in a line, but don't ask for the old fix: that's the backlog coming back
   in through the fix.
5. **Line numbers are the file's**, read from the file as the change leaves it. A line number from
   `change.diff` points somewhere else.
6. **Coverage is the change's:** `test-coverage`'s `changed_uncovered.py` over the same diff, instead of
   the service's totals.
7. **Dependencies:** a package the change adds or moves is a finding at its severity. A vulnerable package
   the change didn't touch still fails the build (NuGetAudit as errors), so name it once under
   Dependency Vulnerabilities as already failing, not as this change's finding.
8. **End with one line:** "Outside this change: N findings (scan and reviewers), not reviewed here.
   `/full-review` without `--diff` covers the whole service." No list: a merge request is judged on what
   it brings.

## Reviewing the whole service

First run the **known-traps scan** over the service (`skills/known-traps/`, read-only, a few seconds):
```bash
python ~/.claude/skills/known-traps/scripts/scan_traps.py <service folder>
```
Give its table to all three reviewers. Each confirms or dismisses the findings in its area, reading the
skill named in the table, and says which. A finding nobody confirms or dismisses stays in the report as
found by the scan.

Then run three review agents in parallel:

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
