# The QA eval without a story

The same service, planted bugs and grading as `../fix-qa` (its `tools/grade-one.sh` and `mutants/`), but the
run gets no `story.md`: only a one-paragraph description of what the service does. It tests the no-story
mode of `qa-engineer` and `test-design` (criteria inferred from the request, the code and the diff).

## The bar, written before any run (2026-11-21)

- **Pilot: one rules-only run, then one with-proven run** (`route.txt`), `claude-opus-5-5` at medium.
- **The mode works** when the with-proven run catches at least 6 of the 8 planted bugs with R2 (its tests
  pass on the correct service), every inferred criterion is marked `(inferred)` in a `story.md`, and the
  designed edge cases are `EC` lines with tests.
- **It earns its place over the model alone** only if it catches at least 2 more than the rules-only run.
  With story, rules alone caught 7 of 8 (`../fix-qa`), so a tie here is the likely result and is reported
  as such.
- One run per arm: "no difference shown", not "no difference".

## The pilot (2026-11-21, at da0984e)

| Run | Bugs caught | R1, R2 | Tests | Cost | Time | `story.md` |
|---|---|---|---|---|---|---|
| `runs/rules-1` (rules only) | 8 / 8 | pass | 135 | $1.11 | 7.4 min | none |
| `runs/proven-1` (with the harness) | 8 / 8 | pass | 176 | $2.99 | 17.0 min | 14 criteria, all `(inferred)`; 57 `EC` lines |

Graded locally with `../fix-qa/tools/grade-one.sh`; CI grades both copies again (`fix-evals.yml`, job
`grade-qa`, as `nostory-rules-1` and `nostory-proven-1`).

**Against the bar:**
- **The mode works:** yes. The with-harness run caught every bug with R2, marked every criterion it
  inferred, and wrote each edge case as an `EC` line; `trace_matrix.py --require-all` passes on its
  `story.md` and tests, with no orphan.
- **It earns its place over the model alone:** not shown. Both runs caught 8 of 8, so the fixture can't tell
  the arms apart, as the bar expected. The harness run cost 2.7 times as much and took 2.3 times as long.
- What the harness run gives that the score doesn't count: a `story.md` that lists, as questions, the
  behaviour it pinned but thinks may be unintended, for the team to confirm. Its value is for the next
  reader, not for this grade.

One run per arm: no difference shown. A fixture with subtler bugs would be needed to say more; this one is
kept as the check that the no-story mode works.
