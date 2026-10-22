# Results: the diff-mode review eval

## 2026-10-22 (proven-dotnet 0.2.20, the release that added `--diff`)

Nine reports (three runs × code, security, DBA), graded by a fresh agent against `key.md`.

| | Result |
|---|---|
| Change findings found (C1 CSV, C2 national ID logged, C3 the reached IDOR) | **27 / 27**: every report found all three |
| Old flaws raised as this change's work | 3, all the same one: each security report's fix for the new date-range error also asks for the old error middleware to be replaced |
| "Outside this change: N" line | 9 / 9, but N isn't stable: the same reviewer counted 9, 16 and 6 |
| Every finding labelled wrote / reaches / breaks | 6 / 9 (the misses are four Suggestions) |
| Line numbers in `ReadingExport.cs` right | 7 / 9: two reports used the diff's line numbers, 25 too high |
| The change's other real problems (the `DateTime` kind from the query string, no range limit, the value formatted in `ar-SA`) | 9 / 9 |

None of the twelve old flaws was listed as a finding. Before diff mode, a review of this branch would
have reported all of them again, so each merge request would carry the service's whole backlog.

## What it means

- **Diff mode does what it says.** Every reviewer found every flaw the change brings, including the one
  it only reaches, and left the backlog out.
- **Two defects, both fixed after the eval** (`/full-review` and the three reviewers, the same release as
  this file):
  - A fix for a change finding may say it also closes an old flaw, but it doesn't ask for the old fix:
    that's how H12 crept back in three times.
  - Line numbers are the file's, read from the file, never the diff's.

  The numbers above are from before those fixes. Re-running them now would be tuned to this eval, so a
  later fixture says whether they hold.
- **Severity drifts between runs.** The CSV finding was Critical in one report and a Suggestion in
  another. Consolidation settles the severity, so the merged review is the one to judge, not a single
  reviewer's line.
- **The count isn't a number to rely on.** It tells the developer there's more; `/full-review` without
  `--diff` is how to see it.

## B3, a refutation pass: not built

The idea was to send every Critical and Warning to verifiers that try to disprove it, and drop what they
can't confirm. It was to be measured first. On the fixtures we have, there's nothing for it to remove:
`full-review-hard`'s reviewers flagged none of its four distractors, and the extra findings in these
nine reports (layering, missing tests, `Cache-Control`, a PHI export audit, a stable sort) are real
problems with the change, not mistakes. A verifier pass would cost about a review's worth of tokens and
change nothing here. It waits for a fixture made from real, noisy code, where false positives exist to
be counted.
