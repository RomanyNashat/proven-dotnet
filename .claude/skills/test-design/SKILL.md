---
name: test-design
description: "Design a story's test cases before the code: number the acceptance criteria, rank risks, use decision tables, boundaries or state transitions, trace each criterion to a test."
version: 1.0.1
---

# Test design: the cases before the code

A test written after the code tests what the code does. A case designed from the story tests what the
story asks for, and it is the contract the implementation has to meet. This skill turns a story into
those cases, by technique rather than by guesswork, and proves every criterion has a test that passed.

Use it before `tdd-guide` or `/tdd` starts on any story with acceptance criteria. The tested sample is `tests/SkillSamples.Tests/TestDesign/`: the story, the rules and the tests
below.

## 1. Number the criteria

Put the story next to the tests as `story.md`, its criteria as `AC1: ...` lines, in the story's own words.
A criterion that can't be tested as written ("fast", "user friendly", "handles errors") is not guessed at:
write the question it leaves open and ask it before designing.

## 2. Rank the risks

For each area the story touches, score how likely it is to go wrong (1 to 3) and how much harm it does if
it does (1 to 3). In this domain, harm 3 is a wrong patient, a record shown to the wrong person, a consent
skipped, or money. Spend the cases where the product is high:

| Score | Cases |
|---|---|
| 6 to 9 | every technique below that fits, and a story test (Given / When / Then) for the worst way it fails |
| 3 to 4 | the one technique that fits the criterion's shape |
| 1 to 2 | one case on the main path |

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

## 4. Pick the level

The lowest level that can fail for the right reason:
- **unit:** a rule;
- **integration** against the real engine in Testcontainers (`testing-integration`): a database
  constraint, a transaction, concurrency, a query's results;
- **WebApplicationFactory:** headers, auth, routing, ProblemDetails;
- **end to end:** only what crosses services.

A unique index that stops a double booking can't be proven with a mock.

## 5. Write the cases as tests, each naming its criteria

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

It also lists tests that name a criterion the story doesn't have, since a typo there hides a gap.
`--require-all` exits 1 unless every criterion is covered, or proven with `--trx`, so it can gate CI.
`--json` is for a tool that reads it. It handles CRLF, a BOM, attributes on the method's line and class
traits; `bin` and `obj` are skipped. Tested in `tests/testdesign` against the sample and a real TRX.

## Rules
- Cases come from the story, before the code, and the developer writes them failing first.
- Every criterion has at least one test that names it; every high risk has a story test.
- The technique fits the shape: a table for rules, edges for ranges, every pair for states.
- A criterion that can't be tested as written becomes a question, never a guess.
- A test is kept only once it has been seen to fail for the right reason.
