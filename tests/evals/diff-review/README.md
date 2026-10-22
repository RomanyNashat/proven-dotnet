# Diff-mode review eval: does a review stay on the change?

`/full-review --diff` should report only what a merge request writes, reaches or breaks, and count the
rest. This eval checks that on a change made for it.

- The base is `../fix-lab/HomeReadings`, as it is: thirteen planted flaws the change doesn't touch.
- `change.diff` (apply with `git apply` in a copy of the base) adds a CSV export for the doctor, with
  three flaws of its own: unescaped CSV fields (formula injection), the national ID in a log line, and a
  "share one reading" route that calls the old query with no owner check (the change reaches an old IDOR).
- `key.md` is the answer key, which no run sees.
- `scan.md` is the known-traps scan with `--diff`, given to every reviewer.
- `runs/run-<n>/`: the three reviewers' reports for each of three runs, launched in parallel from the
  main session with the instructions `/full-review --diff` gives them.
- `grade.md`: a fresh agent's grading against the key, each verdict with the line that decides it.

Results in `results.md`.
