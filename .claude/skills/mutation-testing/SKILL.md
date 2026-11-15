---
name: mutation-testing
description: "Mutation testing with Stryker.NET: would the tests fail if the code were wrong? Score a change's lines, name the edges no test checks."
version: 1.0.0
---

# Mutation testing: would the tests notice?

Coverage says which lines ran. It doesn't say whether a test would fail if a line were wrong. Mutation
testing does: Stryker.NET makes one small change at a time (`>=` becomes `>`, a condition is negated, a
block removed), runs the tests, and records whether they noticed.
- **Detected:** the change was killed, or timed out.
- **Missed:** it survived, or no test ran that line (no coverage).

The score is detected out of detected plus missed.

The tested sample is `tests/mutation/Sample`: a refill rule, a strong test project and a weak one. The weak
tests run every line of the rule, so coverage is full, and still score 71%. The two changes they miss are
exactly the edges they never check:

| Where | Was | Became | Status |
|---|---|---|---|
| RefillWindow.cs:16 | `refillsLeft <= 0` | `refillsLeft < 0` | Survived |
| RefillWindow.cs:19 | `today >= runsOut.AddDays(-DaysBefore)` | `today > runsOut.AddDays(-DaysBefore)` | Survived |

The strong tests (both edges of the window and of the refills left, the `test-design` way) score 100%.
CI runs Stryker on both and checks those numbers.

## Run it on a change

Stryker is a dotnet tool; install the pinned version outside the repo, so the service gets no new file:

```bash
dotnet tool install dotnet-stryker --version 5.1.0 --tool-path ~/.dotnet/stryker
cd tests/Clinic.Tests                        # the test project; Stryker finds the project it tests
~/.dotnet/stryker/dotnet-stryker --since:origin/main --reporter json --reporter cleartext --output ../../StrykerOutput
python ~/.claude/skills/mutation-testing/scripts/mutation_report.py ../../StrykerOutput/reports/mutation-report.json --since origin/main --min-score 80
```

- `--since:origin/main` makes Stryker mutate only the files the branch changed (checked in a throwaway
  repo: one changed file of two was mutated). Leave it off for a whole project.
- `mutation_report.py --since origin/main` narrows to the **lines** the branch changed, so an untouched
  method in the same file doesn't count against the change. It prints the score and a table of the changes
  the tests missed, with file and line, what the code was, and what it became.
- `--min-score N` exits 1 below N, so it can gate a pipeline.
- `--json` is for a tool that reads it.
- With no mutants on the changed lines, it says so and passes.

## Read the result

- **A survived mutant on a comparison is a missing edge.** Write the test on the edge and one step beyond
  it (`test-design`, boundary values).
- **A survived removal or negation is an outcome nobody asserts.** Usually the test checks that the call
  succeeded but not what it did: assert the state or the side effect.
- **No coverage is a line no test runs.** That's a coverage gap first (`test-coverage`).
- **An equivalent mutant** changes nothing observable. Leave it, and say why in the review; don't write a
  test that pins how the code is written.

A score is a signal for review, not a target to chase: 100% on a rule that matters, and a lower bar on
plumbing, are both reasonable. A team that gates on it sets the bar once, in its pipeline, and raises it
only on purpose.

## Cost

Each mutant is a test run. A small rule with a dozen mutants takes about 20 seconds; a whole service takes
minutes to hours. So run it on the change (`--since`) in a pull request, and on the whole project now and
then. `--mutate "**/Booking/*.cs"` narrows to a folder.

## Rules
- Judge a pull request on the lines it changed, not on the whole project.
- Every survivor on a business rule gets a test, or a written reason it's equivalent.
- Never chase the score by testing how the code is written; test what it does.
- Stryker is pinned to a version and installed outside the repo.
