# Scope pilot: does the reviewer need a rule to name changes the task didn't need?

`change.diff` is the diff-review export with two planted changes the story doesn't need:
- `ReadingNotifier.cs`: `waiting` renamed `pendingAlerts`, and the batch quietly raised from 50 to 100.
- `CareTeamClient.cs`: an optional `priority` parameter no caller passes, sent to the care team.

The bar (`bar.md`) was written before any run. Each arm is one headless `code-reviewer` run (Opus) on a
copy of `../../fix-lab/HomeReadings` with the diff applied, the prompt in `prompt.txt`, read-only tools.

| Arm | Names both as outside the task? | Other |
|---|---|---|
| code-reviewer at 0.3.43, told the story (`report-0.3.43.md`) | Yes, both, "should go in a separate MR" | $0.83 |
| with a scope rule added, told the story (`report-with-rule.md`) | Yes, both | also called `.gitignore` out of scope (minor); $0.60 |
| code-reviewer at 0.3.43, no story (`report-0.3.43-no-story.md`) | Flags both as problems, and says to split the batch change from the rename | $0.58 |

**Result:** the baseline already does what the rule asked, so by the bar the reviewer rule isn't added.
`tdd-guide` keeps two lines on the producing side (refactor only what the task wrote; list follow-ups,
don't do them): writing the change is where the extra lines come from, and this pilot doesn't measure it.
One run per arm, so no noise floor: the result is "no difference shown", not "no difference".

**Found on the way:** `../change.diff` was a `-U0` diff (no context lines), and a plain `git apply` puts
zero-context hunks at the end of the file instead of at their line numbers: the endpoints landed after the
class's closing brace and the registration after `public partial class Program;`. Every run here reported
the build errors. They don't touch the scope findings. The base hadn't changed; the diff is regenerated
with context in 0.3.49, and `change.diff` in this folder (also `-U0`) needs `git apply --unidiff-zero`.
