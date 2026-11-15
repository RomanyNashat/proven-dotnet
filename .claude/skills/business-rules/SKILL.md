---
name: business-rules
description: "Mine the business rules a service's code enforces into Given/When/Then, each pinned by a test that passes on today's code."
version: 1.0.0
---

# Business rules: what the code really decides, written down and pinned

Old services hold rules nobody wrote down: who may cancel, until when, how much comes back. This skill
finds them in the code, writes each as Given/When/Then in the business's words, and pins each with a test
that passes on the code as it is today (a characterization test). The tests are then the safety net for a
refactor or an upgrade, and the rules are what `story-check` and `qa-engineer` check a new story against.

It describes; it doesn't judge. A rule that looks wrong is still written and tested as the code does it,
with its question beside it. Fixing it is a story of its own.

## 1. Find the lines that decide something

```bash
python ~/.claude/skills/business-rules/scripts/rule_candidates.py <code folder>
```

It lists every candidate line: a guard (`if` that returns or throws), a thrown exception, a `RuleFor`, a
status change, a limit (a numeric constant or a `TimeSpan` field), a calculation. Test projects and
comments are left out. The list is where to look, not the answer: one rule can span two candidates, and
some candidates aren't rules.

## 2. Write each rule

For each candidate, read the code around it and what calls it (`find_callers`), then write it in
`rules.md` beside the tests:

- **A rule** is a line `R1: Given ..., when ..., then ... (File.cs:25 `the code`)`, in the words of the
  people who use the service ("a visit that starts in less than 2 hours"), not the code's
  (`left < Cutoff`). The backticked code is how the line is found again once lines move.
- **The order matters, so say it.** When one rule only applies after another passed ("asked only once the
  visit is outside the cutoff"), the rule says so, and a test proves it.
- **Not a rule:** a constant a rule uses, plumbing, logging. It goes under `## Not rules` as
  `File.cs:19 `the code`: why`, so the list stays complete.
- **A rule that surprises** (rounding down, a limit that looks off by one) goes under
  `## Questions the rules raise`. It is still written and tested as it is.

<!-- sample: tests/SkillSamples.Tests/BusinessRules/rules.md -->
```markdown
- R4: Given a visit that starts in less than 2 hours, when the patient cancels it, then the answer is TooLate; at exactly 2 hours it may still be cancelled. (VisitCancellation.cs:33 `left < Cutoff`)
- R5: Given a patient who cancelled 2 visits in the last 30 days, when they cancel a third, then the answer is NeedsClinicCall and nothing is refunded; it is only asked once the visit is outside the cutoff. (VisitCancellation.cs:35 `>= CancellationsBeforeCall`)
- R6: Given a patient who cancels in time, when the visit is 24 hours or more away, then all they paid comes back; closer than that, half, rounded down to the smallest unit. (VisitCancellation.cs:38 `var refund =`)

## Questions the rules raise
- R6 rounds the half refund down (12.625 becomes 12.62): is that meant, or should it round to the nearest?
```

## 3. Pin each rule with a test that passes today

One test or more per rule, carrying `[Trait("Rule", "R4")]`, named `Method_Scenario_Expected`. Test the
rule's edges, a tick either side, and the order between rules. Give the tests that tell a real situation
`[Trait("Kind", "Story")]` (Given / When / Then in comments), and at least one rule a production test
(the slim image: UTC, no time-zone database, no ICU).

<!-- sample: tests/SkillSamples.Tests/BusinessRules/VisitCancellationRulesTests.cs -->
```csharp
    [Theory]
    [InlineData(-1, CancelOutcome.TooLate)]
    [InlineData(0, CancelOutcome.Cancelled)]
    [Trait("Rule", "R4")]
    public void Cancel_AtTheTwoHourCutoff_TheEdgeMayStillCancel(long ticks, CancelOutcome expected) =>
        Cancel(VisitIn(VisitCancellation.Cutoff + TimeSpan.FromTicks(ticks))).Outcome.Should().Be(expected);
```

Run them. **They must pass on the current code.** A test that fails means the rule or the test is wrong,
never the code: rewrite the rule from what the code does.

## 4. Check before showing it

```bash
python ~/.claude/skills/business-rules/scripts/rule_candidates.py <code folder> --check rules.md
dotnet test <test project> --logger "trx;LogFileName=rules.trx" --results-directory .qa
python ~/.claude/skills/test-design/scripts/trace_matrix.py rules.md <test folder> --prefix R --trait Rule --trx .qa/rules.trx --require-all
```

The first refuses a candidate that is neither a rule nor a not-rule, and a rule whose code isn't at its
line (within 3 lines). The last refuses a rule with no test, or a test that didn't pass. Show the rules,
the questions, and the two checks' output.
