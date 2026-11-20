# Effort per agent: the bar, written before any run (E4)

The question: should an agent name its own effort (low, medium or high) instead of taking the session's?

The fixture: round five (`fix-labs`), the with-proven arm, the session at medium as before. Only the
subagents' effort changes (`EVAL_SUBAGENT_EFFORT`, which sets `CLAUDE_CODE_SUBAGENT_EFFORT`). At medium the
arm scored 24/24 findings and 18/18 regressions, at $8.70 to $10.60 a run. That ceiling means this fixture
can show a lower effort losing findings, or a cost changing, but not a higher effort finding more.

- **Pilot: subagents at low, one run.** If it keeps every finding and regression, run two more at low.
- **Low earns a place** (some agents could name `effort: low`) when all three low runs keep 14/14 and cost
  at most 70% of the medium runs. A loss in any run keeps medium for every agent.
- **High: one run**, for its cost only. It can't score higher here, so no agent moves to high on this
  fixture; that needs a fixture the arm doesn't already pass.
- **Same harness on both sides.** Round five's medium runs were on 0.3.4; the harness has changed since, so
  medium gets one run on the same ref as the low and high runs, for cost.
- Noise floor: round five's medium runs varied from $8.70 to $10.60; a cost difference inside that spread
  is "no difference shown".
