# The QA eval: do the tests catch a broken story?

A run gets a correct service (`Pharmacy/RefillDesk`), its story (`Pharmacy/story.md`, AC1–AC8) and a test project with one test, and writes the tests for the story (`task.md`). It never sees what it is graded on.

**How a copy is graded** (`tools/grade-one.sh`):
1. its tests must build (R1) and all pass on the correct service (R2);
2. then each of the ten planted bugs (`mutants/`, one changed `Desk.cs` each) replaces the service in turn;
3. a bug counts as caught (M01–M10) when at least one test fails on that copy.

A test that fails on the correct service makes every catch meaningless, so R2 failing scores no bug.

| Bug | What is wrong | Caught by |
|---|---|---|
| M01 | the pharmacy's day is taken in UTC (AC8) | a request just after midnight in Riyadh |
| M02 | the first day of the refill window is refused (AC3) | the window's edge |
| M03 | a prescription with no refills left is accepted (AC2) | zero refills |
| M04 | a caregiver's controlled request skips approval (AC5) | controlled × caregiver |
| M05 | a delegation's last day is refused (AC1) | the delegation's last day |
| M06 | a request waiting for approval doesn't count as open (AC4) | a second request while one is pending approval |
| M07 | a request waiting for approval can be dispensed (AC6) | every pair of statuses |
| M08 | refills go down on approval, not on dispensing (AC7) | refills after approval and after dispensing |
| M09 | the expiry date itself is refused (AC2) | the expiry day |
| M10 | two requests at the same moment both open (AC4) | two requests at once, repeated |

The copies, checked in CI (`fix-evals.yml`, job `grade-qa`):

| Copy | Bugs caught | R1, R2 |
|---|---|---|
| `original` (one test) | 0 / 10 | pass |
| `reference` (60 tests: decision table, edges, every pair of statuses, Riyadh midnight, 200 rounds of 8 requests at once) | 10 / 10 | pass |

M10 is caught by the reference 5 runs out of 5, and its concurrency test passed on the correct service 3 runs out of 3.

## The bar, written before any run (2026-11-15)

- **Arms:**
  - rules only: `core.md` and the rules;
  - with the harness: `route.txt`, which sends the run to `qa-engineer` and the `test-design` skill.
- **Runs:** `claude-opus-5-5` at medium, three per arm, after a pilot.
- **Pilot first:** one rules-only run. If it catches 9 or 10 of the 10, this fixture is too easy to tell the arms apart: redesign it (subtler bugs) before running the rest.
- **Win:** the harness arm catches at least 2 more bugs per run than rules only, on average, with R2 in every run, and the difference is bigger than the noise floor (two identical harness arms, measured once).
- **Falsifier:** the harness arm catches no more than rules only. Then the QA role doesn't earn its cost on this kind of story, and that is the result.
- **Also reported:** cost, time, and the number of tests per run. More tests that catch nothing more is not a win.
