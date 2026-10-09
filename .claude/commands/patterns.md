---
name: patterns
description: "Design patterns, two ways. Explain: find the patterns in your code, show how each works here, and judge the fit (fits, over-engineered, misused, missing, built into .NET). Suggest: describe a problem and get the pattern that fits, or none, with a sketch. Read-only."
allowed-tools: Read, Grep, Glob, Agent, mcp__proven-roslyn__workspace_status, mcp__proven-roslyn__find_symbol, mcp__proven-roslyn__find_implementations, mcp__proven-roslyn__get_type_hierarchy, mcp__proven-roslyn__find_callers, mcp__proven-roslyn__find_references, mcp__proven-roslyn__get_public_api
---

## Design patterns: explain or suggest

Uses `skills/design-patterns/` for the standard: the verdicts, the .NET built-ins and the misuse list.
Read it before starting.

```
/patterns explain <file | folder | Type | project> [--brief]
/patterns suggest "<the problem>" [<file | folder>]
/patterns                      → asks which mode
```

Both modes are read-only. Nothing is edited.

### Explain: what patterns are in this code, and do they fit?

For learning a codebase and for reviewing one.

1. **Scope.** A file, a type or a folder of up to about 15 `.cs` files: do it here. A whole project or
   service: launch the **pattern-analyst** agent with the scope and the mode, so the conversation isn't
   filled with file reads, and present what it returns.
2. **Find and confirm** the patterns by structure, with Roslyn for who implements, inherits and calls
   what (the agent's process is the same).
3. **Report:**

```markdown
## Patterns in <scope>

| Pattern | Where | Verdict |
|---|---|---|
| Decorator | `CachedClinicDirectory` wraps `SqlClinicDirectory` | Fits |
| Strategy | `IPriceCalculator` (1 implementation) | Over-engineered |
| State | `Appointment.IsCancelled` / `IsCheckedIn` / `IsCompleted` | Missing |

### Decorator: `CachedClinicDirectory` (Fits)
**How it works here:** <the interface, the wrapped type, where it's registered, who calls it, in 3-5 lines
with the real names>
**Why it fits:** <the cost it removes, with evidence>
**If you removed it:** <what you'd lose>

### Strategy: `IPriceCalculator` (Over-engineered)
**What's there:** ... **Simpler shape:** ... **Evidence:** `find_implementations` → 1

### State: missing on `Appointment`
**The cost today:** <file:symbol where flags contradict or checks repeat> **The pattern:** ... **Sketch:** <short>
```

With `--brief`, print the table and only the findings that aren't **Fits**.

### Suggest: which pattern for this problem?

For designing something new or untangling something that grew.

1. **Restate the problem** in one line. If it's vague, ask one question first.
2. If a path is given, read it, so the suggestion fits the code that's there.
3. **Report:**

```markdown
## Problem
<one line>

## Recommendation: <pattern>, or "no pattern needed"
<why, in terms of this problem and this code>

## Considered: <the runner-up>
<the trade-off in one or two lines, and why it lost>

## Sketch
<short C# in house style: sealed classes, primary constructors, int ids, TimeProvider, CancellationToken,
registration included>

## Next step
<how to apply it>
```

"No pattern needed" is a full answer: say what the simple version is and when you'd revisit.

### After either mode

- **Applying a change:** a refactor of existing code goes to `refactor-cleaner`, through the
  `remediation-parity` gate. New behaviour goes to `/tdd`.
- **A real design decision** (choosing between two architectures, introducing a saga): offer a
  lightweight ADR, as in the ADR section of `CLAUDE.md`.
