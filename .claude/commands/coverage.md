---
name: coverage
description: "Measure test coverage and optionally raise it. Reports line/branch coverage, test counts, and risk-ranked gaps, then offers a dynamic menu to raise coverage (only targets above your current level). Raises are planned in Plan Mode before any tests are written."
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

## Test Coverage

Delegate to the **coverage-analyst** agent.

Measures your test coverage, shows you where the gaps are, and — if you want — raises coverage
by retrofitting tests onto existing code. Just run `/coverage` and follow the prompts.

### The Flow

```
/coverage
  │
  ├── Step 1: Measure (automatic)
  │   ├── Run Coverlet → Cobertura output
  │   ├── Parse line %, branch %, per-class, per-method, uncovered lines
  │   └── Count test files, test cases, test projects
  │
  ├── Step 2: Report
  │   ├── Line coverage % (vs 80% target)
  │   ├── Branch coverage % (vs 70% target)
  │   ├── Test files / test cases / test projects
  │   └── Least-covered classes, ranked by RISK (not just lowest %)
  │
  ├── Step 3: Dynamic raise menu (computed from current coverage)
  │   Example at 62%:
  │     A. Raise to 70%
  │     B. Raise to 80%   (meets CI threshold)
  │     C. Raise to 90%
  │     D. Leave as is
  │   (Only ever offers targets ABOVE your current coverage)
  │
  ├── Step 4: Plan the raise (Plan Mode) — only if a raise is chosen
  │   ├── Select highest-risk classes/methods that close the gap
  │   ├── List specific methods + scenarios to cover + estimated point gain
  │   ├── Flag any probable bugs spotted while reading
  │   └── Wait for approval (no tests written yet)
  │
  └── Step 5: Write characterization tests — after approval
      ├── Lock in current behavior (test-after, NOT TDD)
      ├── Cover both sides of every branch
      ├── Flag (never silently encode) probable bugs
      └── Re-run coverage, confirm target hit, report new numbers
```

### Why "characterization tests" and not TDD

The code already exists, so this is **test-after**: writing tests that document and protect
current behavior. That's a different technique from `/tdd` (which is test-first — write a
failing test, then the code). This command never uses the tdd-guide agent; the coverage-analyst
writes the retrofit tests itself using the characterization method.

### The dynamic menu

The raise options are computed from your actual coverage — you'll only ever see targets above
where you are now. At 20% you might see 40/60/80; at 78% you'd see 80/90/100. The option that
reaches 80% is always flagged as the CI threshold (`rules/testing.md`: 80% line / 70% branch).

### Safety

- **Plan Mode before writing.** Raises are always planned and approved before any test is written.
- **Bugs are flagged, not blessed.** If existing code looks buggy, the agent surfaces it and lets
  you decide whether to assert correct behavior (test fails, documenting the bug) or characterize
  current behavior with a clear comment — never silently locks in a bug as "expected".
- **Read-only on your config.** Coverage artifacts aren't committed; test configuration isn't
  changed without asking.

### Related

- `/full-review` includes a read-only coverage check (reports numbers only, never raises)
- `/tdd` is for test-first development of new code
- `/verify` runs the full build + test + coverage gate before a PR
