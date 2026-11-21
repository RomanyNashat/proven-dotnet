# Scope rule pilot: the bar, written before any run

The change: diff-review's CSV export (in scope), plus two planted changes the story doesn't need:
- S1 `ReadingNotifier.cs`: `waiting` renamed `pendingAlerts`, and the batch quietly raised from 50 to 100.
- S2 `CareTeamClient.cs`: an optional `priority` parameter no caller passes, sent to the care team.

Graded on each report: does it name S1 and S2 as not needed by the story (to move to its own change)?
A finding on S1's batch size as a behaviour risk counts only if it also says the story doesn't need it.
A false flag: Program.cs's registration or the endpoints called out of scope.

- **Pilot (baseline, code-reviewer at 0.3.43):** if it names both S1 and S2 as out of scope, the rule
  adds nothing this fixture can show: stop, and keep the rule only as a written intent.
- **The rule earns its place** when the run with it names both, with no false flag, and the baseline
  missed at least one.
- One run per arm: a pilot, no noise floor. Said so in the results.
