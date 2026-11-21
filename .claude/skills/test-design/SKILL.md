---
name: test-design
description: "Design a story's test cases before the code: numbered criteria, risks, decision tables, boundaries, state transitions, each criterion traced to a test."
version: 1.4.0
---

# Test design: the cases before the code

A test written after the code tests what the code does. A case designed from the story tests what the
story asks for, and it is the contract the implementation has to meet. This skill turns a story into
those cases, by technique rather than by guesswork, and proves every criterion has a test that passed.

Use it before `tdd-guide` or `/tdd` starts on any change: a story with acceptance criteria, or a request
with no story at all (section 1). The tested sample is `tests/SkillSamples.Tests/TestDesign/`: the story,
the rules and the tests below.

## 1. Number the criteria

Put the story next to the tests as `story.md`, its criteria as `AC1: ...` lines, in the story's own words.
A criterion that can't be tested as written ("fast", "user friendly", "handles errors") is not guessed at:
write the question it leaves open and ask it before designing. The cases the story leaves open altogether
(empty, failure, duplicates, existing data) are found before this, by `story-check`.

**No story?** A request ("add an endpoint that cancels a visit") still has criteria; they are just not
written down. Write them from three places, each line starting `(inferred)`:
- the request's own words;
- what the code around it already enforces: its validations, limits and states (`business-rules` finds
  them), which a change must keep;
- what the diff changes, when there is one: every behaviour it adds, removes or moves.

`AC1: (inferred) a visit can be cancelled up to two hours before it starts`. Ask only what the code can't
answer. The marker stays until the developer confirms the line, so a reader knows it is an assumption, and
`/qa-scan` counts the inferred ones on its verdict line.

## 2. Rank the risks

For each area the story touches, score how likely it is to go wrong (1 to 3) and how much harm it does if
it does (1 to 3). In this domain, harm 3 is a wrong patient, a record shown to the wrong person, a consent
skipped, or money. Spend the cases where the product is high:

| Score | Cases |
|---|---|
| 6 to 9 | every technique below that fits, and a story test (Given / When / Then) for the worst way it fails |
| 3 to 4 | the one technique that fits the criterion's shape |
| 1 to 2 | one case on the main path |

**The code's strength follows the same score.** A guard, a check, a retry or an extra layer is there
because a risk of 6 or more asks for it: a wrong patient, a record shown to the wrong person, a skipped
consent, money. A risk of 1 or 2 gets the plain code and one test. A guard that no risk asks for is
over-engineering, the same as a missing guard on a high risk is a bug, and `code-reviewer` names both.

**Where to look first, when there's no story or the change is wide:** the files changed most lately and
fixed most often are where the next bug is.
`python ~/.claude/skills/test-design/scripts/risk_map.py <repo> --since "90 days ago"` ranks them from the
git history: how often each changed, how many of those changes were fixes, and how recently.

## 3. Pick the technique by the criterion's shape

- **A rule with conditions → a decision table.** One row for every combination that can change the
  outcome, so a combination nobody thought about has a row and an expected answer.
- **A range, a limit, a length or a date → boundary values.** On the edge and one step either side, where
  the step is the unit the code compares in: a tick for a time, a day for a date, a character for a length
  (`HasMaxLength`).
- **A status → state transitions.** Every pair of states, not only the moves that are allowed: the move
  nobody asked for is the bug.
- **Several independent inputs** (language, app version, role) **→ pairwise.** Every pair of values at
  least once, not every combination.

Then run every criterion past the cases that go wrong most often here, and add the ones that apply:
- the clinic's date against UTC midnight;
- Hijri against Gregorian;
- Arabic text against a length limit;
- a double submit, or two requests at once;
- another person's id (ownership);
- an old app version that can't be updated;
- a setting that is empty or missing;
- a dependency that is slow or down.

## 3a. Questions every change gets asked

The techniques above come from the criterion. These come from experience, and a tester asks them of
anything, criterion or not. Take the ones that fit; each that finds something becomes a case.
- **Size:** none, one, many; too small, just right, too big; the first, a middle one and the last.
- **Some, none, all:** of the permissions, the records, the selected items.
- **Time:** interrupted half way (a timeout, a cancel, a restart, the phone losing signal); the same
  thing twice; two at once; out of order (cancel before confirm, undo after the next step).
- **Follow the data:** create it, then read, change and delete it, and see that every place that shows it
  agrees; then the same for each thing it belongs to or owns.
- **Broken rules:** a required field missing, two fields that must agree but don't, a duplicate, a value
  of the wrong kind that still parses.
- **The surroundings:** the other language, an old app version, the clinic's time zone, a dependency
  slow or down, a setting empty.

## 3b. How a test knows the answer is wrong

A case needs an expected answer, and the criterion doesn't always give it. Then compare with what else
says what's right, and write down which one the test uses:
- the same kind of thing elsewhere in the service (two screens that list visits should agree);
- what it did before the change, and what old app versions still expect;
- the data's own rules (a total equals its lines; a count matches the list);
- the API contract and its documented errors;
- the regulation (consent, retention, who may see what).

When none of them settles it, the answer is a question in the report, never a guess written into a test.

## 4. Pick the level

The lowest level that can fail for the right reason:
- **unit:** a rule;
- **integration** against the real engine in Testcontainers (`testing-integration`): a database
  constraint, a transaction, concurrency, a query's results;
- **WebApplicationFactory:** headers, auth, routing, ProblemDetails;
- **end to end:** only what crosses services.

A unique index that stops a double booking can't be proven with a mock.

## 5. Write the cases as tests, each naming its criteria

**Every edge case you designed is a line of its own** in `story.md`, after the criteria: `EC1: AC2,
boundary: booked exactly two hours before, it is allowed`. It starts with the criterion it belongs to and
the technique that found it, and its test carries `[Trait("EC", "EC1")]` beside the criterion's. A
criterion's main-path test makes it "covered"; only the edge's own line shows that the edge has a test.

`MethodName_Scenario_ExpectedBehavior` (`testing-tdd`). `[Trait("AC", "AC2")]` on the method, or on the
class for all of its tests. The decision table for who may book a visit:

<!-- sample: tests/SkillSamples.Tests/TestDesign/VisitBookingTests.cs -->
```csharp
    // Decision table: who books (self / other), linked guardian (yes / no), patient adult on the visit day.
    [Theory]
    [InlineData("self", false, true, BookingDecision.Allowed)]
    [InlineData("self", false, false, BookingDecision.NeedsGuardian)]
    [InlineData("other", true, false, BookingDecision.Allowed)]
    [InlineData("other", true, true, BookingDecision.PatientBooksThemself)]
    [InlineData("other", false, false, BookingDecision.NotYourDependent)]
    [InlineData("other", false, true, BookingDecision.NotYourDependent)]
    [Trait("AC", "AC1"), Trait("AC", "AC2"), Trait("AC", "AC3")]
    public void Decide_WhoBooksForWhom_FollowsTheDecisionTable(string booker, bool linked, bool adult, BookingDecision expected)
    {
        var patient = adult ? Adult : Child;
        var by = booker == "self" ? patient : Guardian;

        VisitBooking.Decide(by, patient, linked, Tomorrow, Now).Should().Be(expected);
    }
```

Every pair of statuses, so a move nobody asked for can't slip in:

<!-- sample: tests/SkillSamples.Tests/TestDesign/VisitBookingTests.cs -->
```csharp
    public static TheoryData<VisitStatus, VisitStatus, bool> EveryMove()
    {
        var allowed = new[] { (VisitStatus.Booked, VisitStatus.CheckedIn), (VisitStatus.CheckedIn, VisitStatus.Completed), (VisitStatus.Booked, VisitStatus.Cancelled) };
        var data = new TheoryData<VisitStatus, VisitStatus, bool>();
        foreach (var from in Enum.GetValues<VisitStatus>())
            foreach (var to in Enum.GetValues<VisitStatus>())
                data.Add(from, to, allowed.Contains((from, to)));
        return data;
    }
```

And the story test for the highest risk, the age on the visit day. A dependent turns 18 at midnight in
Riyadh, three hours before midnight in UTC:

<!-- sample: tests/SkillSamples.Tests/TestDesign/VisitBookingTests.cs -->
```csharp
    {
        // Given a dependent born on 10 December 2008, and a visit at 00:30 on 10 December 2026 in Riyadh,
        // which is 21:30 on the 9th in UTC
        var dependent = new Person(4, new DateOnly(2008, 12, 10));
        var visit = new DateTimeOffset(2026, 12, 10, 0, 30, 0, VisitBooking.ClinicOffset);
        visit.UtcDateTime.Day.Should().Be(9);

        // When the guardian books it
        var decision = VisitBooking.Decide(Guardian, dependent, isLinkedGuardian: true, visit, Now);

        // Then the dependent is 18 on the day of the visit at the clinic, so they book for themself.
        // Counting the day in UTC would have made them 17 and let the guardian book.
        decision.Should().Be(BookingDecision.PatientBooksThemself);
    }
```

The same file holds the production test: the clinic's date on an image without tzdata, which works
because Saudi Arabia keeps UTC+3 all year and the code uses that fixed offset.

## 6. Check the tests can fail

A test that can't fail proves nothing. Break each high-risk rule once on purpose and watch its test go red:
in the sample, computing the visit's day in UTC instead of Riyadh fails the story test with "found
Allowed". Put the code back before going on. `mutation-testing` does this for every line a change
touched, with Stryker.NET.

## 7. Trace every criterion to a test that passed

```bash
python ~/.claude/skills/test-design/scripts/trace_matrix.py story.md tests/Clinic.Tests --require-all
dotnet test tests/Clinic.Tests --logger "trx;LogFileName=results.trx"
python ~/.claude/skills/test-design/scripts/trace_matrix.py story.md tests/Clinic.Tests --trx tests/Clinic.Tests/TestResults/results.trx --require-all
```

It prints criterion, status and tests:
- **uncovered:** no test names it;
- **covered:** a test names it;
- **proven:** every result of its tests passed;
- **failing:** one of them failed;
- **not run:** none of its tests are in the results.

Edge cases (`EC` lines) are traced the same way, by default, and an edge that doesn't start with a
criterion the story has is listed: it belongs to nothing. It also lists tests that name a criterion the
story doesn't have, since a typo there hides a gap.
`--require-all` exits 1 unless every criterion and every edge case is covered, or proven with `--trx`, so
it can gate CI.
`--json` is for a tool that reads it. It handles CRLF, a BOM, attributes on the method's line and class
traits; `bin` and `obj` are skipped. Tested in `tests/testdesign` against the sample and a real TRX.

## 8. On the finished branch: `/qa-scan`

`/qa-scan` puts the evidence together before a story ships. The criteria come from `trace_matrix.py` with
the test results, the tests' strength from `mutation-testing` on the changed lines, and what else to
retest from `changed_members.py --since origin/main` and Roslyn's callers. `qa_verdict.py` then says YES
(every criterion proven, every survivor accepted with a reason), NO (each reason named) or CAN'T SAY
(evidence missing, never read as YES). The scripts' tests are in `tests/testdesign`.

## Rules
- Cases come from the story, before the code, and the developer writes them failing first.
- Every criterion has at least one test that names it; every edge case designed is an `EC` line with a
  test of its own; every high risk has a story test.
- No story is no reason to skip this: the criteria are inferred, marked `(inferred)`, and confirmed.
- The technique fits the shape: a table for rules, edges for ranges, every pair for states.
- A criterion that can't be tested as written becomes a question, never a guess.
- A test is kept only once it has been seen to fail for the right reason.
