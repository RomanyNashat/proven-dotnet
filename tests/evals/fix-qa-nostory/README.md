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
