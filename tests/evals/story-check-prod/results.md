# Results

2026-11-15, `claude-opus-5-5` at medium effort, one run per case.

| Case | Result | Cost | Turns | Time |
|---|---|---|---|---|
| `file` | 5/5 | $0.47 | 9 | 71 s |
| `jira` | 4/4 | $0.24 | 1 | 8 s |

- **`file`:** NOT READY with two questions: what the patient sees if the store fails mid-cancel, and which
  calendar and digits a date in the confirmation uses (kept with `not_answered_because`: AC4 picks the
  text's language, not a date's calendar). Every settled case had its quote or line, and the script
  accepted the report on the first run. It also said, under the report, that the code checks the 2-hour
  cutoff before "already cancelled", so AC3 fails inside the window: a real bug in the sample, found
  without being asked.
- **`jira`:** said no Jira tool is connected, wrote nothing, asked for the story's text (or a file), and said
  it would only read Jira once one is connected.

## What changed after a run, and why
- **The grader's ruling check.** It first looked for the literal `Story check: `. The `file` run's reply
  opened with "Story check of `story.md`: NOT READY" and gave both questions word for word. The bar,
  written before the run, was "the reply shows the ruling", so the check now looks for the ruling and for
  every question exactly as the report has it. Regraded, the same run passes all five.
- **The Jira key.** The first `jira` run used another key, which the public repo's leak check refused
  before the push; the key became `PROJ-123` and the case was run again. Both runs passed the same four
  checks.
- The `file` run used the commit before that change; the skill's text and the `file` case are the same in
  both.
