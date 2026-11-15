# The QA eval: do the tests catch a broken story?

A run gets a correct service (`Pharmacy/RefillDesk`), its story (`Pharmacy/story.md`, AC1–AC8) and a test project with one test, and writes the tests for the story (`task.md`). It never sees what it is graded on.

**How a copy is graded** (`tools/grade-one.sh`):
1. its tests must build (R1) and all pass on the correct service (R2);
2. then each of the eight planted bugs (`mutants/`, one changed `Desk.cs` each) replaces the service in turn;
3. a bug counts as caught (M01–M08) when at least one test fails on that copy.

A test that fails on the correct service makes every catch meaningless, so R2 failing scores no bug.

| Bug | What is wrong | Caught by |
|---|---|---|
| M01 | a delegation's days are taken in UTC (AC1, AC8) | a delegation's edge just after midnight in Riyadh |
| M02 | the expiry is checked in UTC (AC2, AC8) | an expiry just after midnight in Riyadh |
| M03 | a delegation for another patient counts (AC1) | a caregiver delegated for someone else |
| M04 | only a caregiver's first delegation for the patient counts (AC1) | an old delegation and a current one |
| M05 | a rejected request still counts as open (AC4) | a new request after a rejection |
| M06 | two dispenses of one request at the same moment both go through (AC6, AC7) | two dispenses at once, repeated |
| M07 | dispensing doesn't start a new supply (AC3) | a request right after a dispense |
| M08 | an approved request still counts as open (AC4) | a new request while one is approved |

The copies, checked in CI (`fix-evals.yml`, job `grade-qa`):

| Copy | Bugs caught | R1, R2 |
|---|---|---|
| `original` (one test) | 0 / 8 | pass |
| `reference` (decision table, edges, every pair of statuses, Riyadh midnight on every dated rule, 200 rounds of 8 requests and of 8 dispenses at once) | 8 / 8 | pass |

## The bar, written before any run (2026-11-15)

- **Arms:**
  - rules only: `core.md` and the rules;
  - with the harness: `route.txt`, which sends the run to `qa-engineer` and the `test-design` skill.
- **Runs:** `claude-opus-5-5` at medium, three per arm, after a pilot.
- **Pilot first:** one rules-only run. If it catches 9 or 10 of the 10, this fixture is too easy to tell the arms apart: redesign it (subtler bugs) before running the rest.
- **Win:** the harness arm catches at least 2 more bugs per run than rules only, on average, with R2 in every run, and the difference is bigger than the noise floor (two identical harness arms, measured once).
- **Falsifier:** the harness arm catches no more than rules only. Then the QA role doesn't earn its cost on this kind of story, and that is the result.
- **Also reported:** cost, time, and the number of tests per run. More tests that catch nothing more is not a win.

## The pilot (2026-11-15): the fixture can't reach the bar

One rules-only run (`claude-opus-5-5`, medium, public `main` 0e589a7): 168 tests, $1.32, 5 minutes. Its
copy is in `runs/rules-1`.

1. **Against the first ten bugs it caught all ten**, with R2. That is the pilot rule's "9 or 10": the
   fixture was redesigned. The first set was the day in UTC, the window's first day, zero refills, a
   caregiver's controlled request, a delegation's last day, pending approval not counted as open, pending
   dispensed, refills down on approval, the expiry day, and two requests at once.
2. **The second set, the eight above, was written without reading the pilot's tests,** from what a careful
   tester could still miss: a rule's day in UTC on one rule only, a delegation for someone else, an older
   delegation listed first, a closed status counted as open, two dispenses at once, a dispense that doesn't
   restart the supply. Graded on the same pilot copy, **it caught 7 of 8**. It missed only M06, two
   dispenses of one request at the same moment.
3. One change to the service on the way was undone: a store method added for a ninth bug broke the pilot's
   own fake store, so the method and that bug were dropped.

**What it means:** a strong model already designs story tests well here. With 7 of 8 caught by rules
alone, the harness arm can add at most one bug per run, and the bar asks for two. So the grid wasn't run:
it could confirm the falsifier or a one-bug difference, never the win. A third redesign would be built
around what this one run missed, which tunes the fixture to the baseline, so it wasn't done.

The QA role's case stands on what this eval doesn't grade:
- tests written by someone other than the implementer;
- every criterion traced to a test that passed;
- a gate on that.

Its detection value stays unshown.
