---
name: test-coverage
description: Test coverage for .NET: Coverlet run, Cobertura parse, raise menu, least-covered detection, characterization tests. Used by coverage-analyst + /coverage.
version: 1.0.0
---

# Test Coverage Skill

This skill contains the knowledge to measure test coverage, present it clearly, and raise
it safely. It powers the `/coverage` command (via the coverage-analyst agent) and provides
the read-only measurement logic that `/full-review` reuses for its coverage check.

**Key distinction:** Raising coverage on existing code is NOT TDD. TDD is test-first — write
a failing test, then write code. Here the code already exists, so we write **characterization
tests** that document and lock in current behavior. This is a different technique with its own
rules (see §4). Never route this work to the tdd-guide agent — it would conflict with that
agent's test-first mandate.

---

## 1. Measuring Coverage

### 1.1 Run Coverlet

The stack already uses Coverlet with Cobertura output (see `skills/code-quality/`). Run:

```bash
# Collect coverage across the solution
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Or via MSBuild for a single output file
dotnet test /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=./coverage/coverage.cobertura.xml
```

This produces a Cobertura XML file (`coverage.cobertura.xml`) — the source of truth for parsing.

### 1.2 Parse Cobertura XML

The Cobertura format exposes everything needed. Key elements:

```
<coverage line-rate="0.62" branch-rate="0.48" ...>   ← overall rates (0.0–1.0)
  <packages>
    <package name="NotificationService" line-rate="0.71" branch-rate="0.55">
      <classes>
        <class name="NotificationService.Kafka.NotificationConsumer"
               filename="src/.../NotificationConsumer.cs"
               line-rate="0.34" branch-rate="0.20">
          <methods>
            <method name="ConsumeAsync" line-rate="0.10" branch-rate="0.0">
          <lines>
            <line number="42" hits="0" branch="false"/>   ← uncovered line
```

Extract:
- **Overall line coverage** = `coverage/@line-rate` × 100
- **Overall branch coverage** = `coverage/@branch-rate` × 100
- **Per-class coverage** — for ranking least-covered classes
- **Per-method coverage** — for pinpointing exactly what to test
- **Uncovered lines** (`hits="0"`) — the concrete gaps

A quick parse with Python (available in the environment) or by reading the XML directly:

```bash
# Find the latest cobertura file
find ./coverage -name "coverage.cobertura.xml" | head -1
```

### 1.3 Count Test Files and Tests

Complement coverage rates with raw counts the developer asked for:

```bash
# Number of test files
find . -path "*Tests*" -name "*.cs" -type f | grep -iE "test|spec" | wc -l

# Approximate number of test cases (Fact + Theory attributes)
grep -rEh "\[Fact\]|\[Theory\]" --include="*.cs" . | wc -l

# Test projects
find . -name "*.csproj" | xargs grep -l -i "Microsoft.NET.Test.Sdk" 2>/dev/null
```

### 1.4 The Coverage Report

Present a clear snapshot:

```
Test Coverage Report
────────────────────────────────────
Line coverage:    62.4%   (target: 80%)
Branch coverage:  48.1%   (target: 70%)
Test files:       34
Test cases:       287
Test projects:    5

Least-covered classes (highest risk first):
  1. NotificationConsumer.cs        34%   (Kafka consumer — high blast radius)
  2. ChallengeProgressCalculator.cs 41%   (core business logic)
  3. StepsAggregationService.cs     52%   (high-traffic path)
```

Risk ranking is not just "lowest %". Weight by: business criticality (domain/application logic
over DTOs/wiring), blast radius (consumers, shared libraries, auth), and traffic (hot paths).
A 40%-covered DTO mapper matters less than a 40%-covered payment calculator.

---

## 2. The Dynamic Raise Menu

The menu is **computed from current coverage** — never hardcoded. Only offer targets ABOVE
the current line coverage, in sensible tiers, plus "leave as is".

### 2.1 Tier Logic

Round current coverage down to a tier boundary, then offer the next tiers:

```
Tiers: 40, 50, 60, 70, 80, 90, 100
Offer the next 3 tiers strictly above current coverage, plus "leave as is".
```

Examples:

| Current | Menu |
|---------|------|
| 20% | A. Raise to 40%  B. Raise to 60%  C. Raise to 80%  D. Leave as is |
| 62% | A. Raise to 70%  B. Raise to 80%  C. Raise to 90%  D. Leave as is |
| 78% | A. Raise to 80%  B. Raise to 90%  C. Raise to 100%  D. Leave as is |
| 88% | A. Raise to 90%  B. Raise to 100%  C. Leave as is |
| 95% | A. Raise to 100%  B. Leave as is |

Rules:
- Never offer a target at or below current coverage.
- Cap at 100%. Note that 100% is rarely worth it — flag diminishing returns above ~85%.
- If already at 100%, congratulate and skip the menu.
- The jump between offered tiers adapts: tight near the top (78 → 80/90/100), standard lower down.

### 2.2 Presenting the Menu

```
You're at 62% line coverage. How would you like to proceed?

  A. Raise to 70%   (~8 points — roughly N methods in the least-covered classes)
  B. Raise to 80%   (~18 points — meets the CI threshold)
  C. Raise to 90%   (~28 points — diminishing returns begin around here)
  D. Leave as is

Pick a letter.
```

Always annotate option B (or whichever hits 80%) as "meets the CI threshold" since
`rules/testing.md` mandates 80% line / 70% branch.

---

## 3. Planning the Raise (Plan Mode)

When the developer picks a raise target, **enter Plan Mode** — plan the work and present it
for approval BEFORE writing any tests. Never auto-write.

### 3.1 Selecting What to Test

To raise from current% to target%, compute the coverage gap and select classes/methods that
close it efficiently, prioritizing by risk:

1. Calculate uncovered lines needed: `(target − current) × total_lines / 100`
2. Walk the least-covered, highest-risk classes from §1.4
3. For each candidate, list the specific uncovered methods and line ranges
4. Stop selecting once the projected gain meets the target (with a small buffer)

### 3.2 The Plan

Present a structured plan the developer approves:

```
Plan: Raise coverage 62% → 80% (+18 points)

Target classes (in priority order):
  1. NotificationConsumer.cs (34% → ~85%)
     - ConsumeAsync: happy path, malformed message, DLQ routing, cancellation
     - HandleRetry: max-retry exhaustion, backoff calculation
     Est. +7 points

  2. ChallengeProgressCalculator.cs (41% → ~90%)
     - CalculateProgress: boundary values, timezone edges, zero-step days
     - ApplyMultiplier: tier transitions
     Est. +6 points

  3. StepsAggregationService.cs (52% → ~85%)
     - AggregateDaily: empty range, partial data, duplicate entries
     Est. +5 points

Approach: characterization tests (lock in current behavior) using xUnit + FluentAssertions 7.x (never 8+ — license),
following skills/testing-tdd patterns. Testcontainers for any integration-level paths
(skills/testing-integration).

⚠️ Potential bugs spotted while reading (will flag, not silently encode):
  - NotificationConsumer.ConsumeAsync swallows exceptions without logging at line 88

Approve to proceed, or adjust the targets.
```

After approval, the developer exits Plan Mode and says go; then the coverage-analyst writes
the tests.

---

## 4. Writing Characterization Tests

This is the core technique for retrofitting coverage. The goal: capture what the code
**currently does** so future changes that alter behavior break a test (a safety net),
WITHOUT blindly blessing bugs as correct.

### 4.1 Method

1. **Read the code** and determine its actual current behavior for representative inputs
2. **Write tests that assert that behavior** — happy path, edge cases, error paths
3. **Cover branches** — each `if`/`switch`/`?:`/`catch` needs both sides exercised
4. **Name by behavior** following the convention `Method_Scenario_ExpectedBehavior`
   (see `skills/testing-tdd/`)
5. **Use the test stack** — xUnit, FluentAssertions 7.x (never 8+ — license), Moq, AutoFixture; Testcontainers
   for integration paths (see `skills/testing-integration/`)

### 4.2 The Critical Nuance — Don't Lock In Bugs

Characterization tests document *existing* behavior. But if the existing behavior is clearly
wrong, blindly asserting it makes the bug permanent and "tested". Handle this carefully:

- If behavior looks correct → write a test asserting it.
- If behavior looks like a **bug** → do NOT silently assert it as expected. Instead:
  - Flag it to the developer with file:line and why it looks wrong
  - Offer two options: (a) write the test asserting *correct* behavior (will fail → documents the bug as a known issue), or (b) write the characterization test asserting *current* behavior with a `// CHARACTERIZATION: current behavior may be a bug — see [ticket]` comment
  - Let the developer decide — never decide silently

Example signals of a probable bug: swallowed exceptions, off-by-one in boundaries, missing null
checks that will NRE, inverted conditions, silent data loss, ignored CancellationToken.

### 4.3 Quality Bar

Retrofitted tests must meet the same bar as any other test:
- AAA structure (Arrange-Act-Assert)
- One logical assertion focus per test
- No test interdependence
- Deterministic (no real time/random/network without control — use TimeProvider, fixed seeds)
- Fast (characterization unit tests should be milliseconds)

---

## 5. Read-Only Mode (for /full-review)

`/full-review` reuses ONLY §1 (measurement). It reports coverage as one section of the review,
with NO menu, NO planning, NO test writing.

Output for the review context:

```markdown
### Test Coverage (read-only)
- Line coverage: 62.4% (target 80%) ⚠️ below threshold
- Branch coverage: 48.1% (target 70%) ⚠️ below threshold
- Least-covered: NotificationConsumer.cs (34%), ChallengeProgressCalculator.cs (41%)
- To raise coverage, run `/coverage`
```

Mark below-threshold values with ⚠️. If coverage meets both thresholds, mark with ✓ and keep
it to a single line. Always point to `/coverage` as the next step — `/full-review` never
raises coverage itself.

---

## 6. Output File Handling

The coverage report is presented inline in the conversation by default. If the developer wants
it saved, write `coverage-report.md` to the project root. The Cobertura XML and any
ReportGenerator HTML output stay in `./coverage/` (keep it gitignored).

Do NOT commit coverage artifacts. Do NOT modify the developer's test configuration without asking.
