# code-health (/health-check) — evals

Mechanical checks only (see `skill-evals`). Run each case in a fresh subagent with proven-dotnet installed
and again without, on `tests/evals/full-review/PharmacyRefills` (its planted findings are listed in
`tests/evals/full-review/evals.md`). Held out: the skill wasn't changed to pass these.

### Case 1: a grade with evidence, and honest gaps
**Prompt:** /health-check on `tests/evals/full-review/PharmacyRefills`.
**Must:** give an overall A–F grade and a grade per category, each with the measurement or file behind it.
**Must:** say coverage can't be measured (there's no test project) and score it as such, naming the gap,
rather than leaving it out of the weighting without saying so.
**Must not:** invent numbers it didn't measure (a coverage %, a duplication %, a maintainability index)
when the tool for it didn't run. "Not measured, because…" is the pass.

### Case 2: security pulls the grade down
**Must:** count the known-vulnerable direct package (`System.Text.Json` 8.0.4) and the SQL injection in
`RefillQueries.SearchAsync` in the security category, and the overall grade can't be A or B with them.
**Must not:** report the `FromSql($"...")` call in `ForPatientAsync` as injection (it's parameterized).

### Case 3: the path to the next grade
**Must:** list the fixes that move the grade most, highest first, each naming a file. The top of the list
includes F1 (injection) and F9 (the vulnerable package) or adding tests.
**Must not:** lead with cosmetic items (naming, formatting) while those are open.

### Case 4: read-only
**Must:** change nothing in the fixture (`git status` clean) and write no tests.

## Scoring
Per run: grade given with evidence (yes/no), unmeasured metrics stated not invented (yes/no), F1 and F9 in
security (yes/no), D1 not flagged (yes/no), next steps ranked by effect (yes/no), fixture unchanged (pass/fail).

Results per run are in `tests/evals/full-review/results.md`.
