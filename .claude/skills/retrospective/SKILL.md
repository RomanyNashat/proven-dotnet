---
name: retrospective
description: The self-audit behind /improve: review the harness (proven-dotnet plus any team layer) against its own rules and find drift — enforcement gaps, orphan/overlapping skills, coverage gaps. Proposes fixes one at a time.
---

# Retrospective — the harness audits itself

Powers `/improve`. This is the automation of how a harness actually evolves: someone notices the output
drifting from what the rules promise, and a rule/skill gets fixed. `/improve` does that *proactively* —
it reviews the harness against its own stated rules and surfaces where reality has drifted from intent, then
walks the fixes one at a time.

It **complements `tools/check-content.py`, doesn't duplicate it.** That script is the *mechanical*
gate in CI (sizes, ASCII, missing references, read-only agents). `/improve` is the *judgment* audit — the things a script
can't check: is this rule actually enforced anywhere? do these two skills overlap? does this command
that edits code call the parity gate? It's read-only and proposes; it never auto-applies.

## What it audits

**1. Enforcement gaps (the most valuable check).**
A rule is stated in `core.md` / `rules/` / the decision log, but nothing actually enforces or applies
it. Cross-reference every stated rule against the skills, agents, commands and reviewers:
- Rule says "never X" → is there a reviewer check or skill that catches X? (e.g. the migration-runner
  ban → the code-reviewer flag; the jargon rule → the code-reviewer readability flag.)
- A standard exists as a skill but no command/agent uses it → it's dead intent.
- A recorded decision that a skill now contradicts → drift; flag the contradiction.

**2. Orphan and overlapping skills/agents.**
- **Orphans:** a `skills/X` referenced by no agent or command (dead weight spending budget). (`check-content.py`
  reports *missing* referenced skills; `/improve` catches the reverse — skills nobody references.)
- **Overlap:** two skills or two agents with descriptions so similar they'd compete for selection —
  the mis-triggering risk. Flag pairs that should be merged or sharpened.

**3. Coverage gaps (rules the harness states about itself, applied to itself).**
- A command that **edits code** but doesn't route through `remediation-parity` (the parity gate).
- A command that produces **shareable writing** but doesn't invoke the `simplicity` layer.
- A **side-effect command** (writes code, commits) without `disable-model-invocation` or the never-
  self-commit guard.
- A code-navigation agent that doesn't prefer the **Roslyn MCP** (with graceful fallback).

**4. Description health (judgment, beyond the budget number).**
- Descriptions near the 250 ceiling that are also *vague* (weak triggers) — the dangerous combination.
- Descriptions that don't lead with the trigger (what the skill is FOR) in the first clause.
- Total budget headroom trend — if it's chronically at the ceiling, that's the signal to move
  service-specific skills into that service repo's `.claude/skills/`, not to keep trimming.

**5. Recurring friction (when session context is available).**
If recent session history/transcripts are readable, look for patterns: the same correction given
repeatedly (a rule that isn't landing), the same manual step done by hand each time (a candidate for a
command), the same skill mis-firing. Where it isn't available, skip this and say so — don't invent it.
Lessons files (`~/.claude/lessons/*.md`, written by `/lessons`) are the best source here: items still
`open`, and the same lesson captured in more than one file, which means a fold-in didn't stick.
`~/.claude/agent-runs.log` (one line per finished subagent: name, tool calls, Roslyn calls) shows which
agents never run and which never touch Roslyn.

## How it reports — one fix at a time (plan-first)

1. **Scan** everything above (read-only) and build a **prioritized findings list**: each finding is
   *what drifted*, *the evidence* (the specific rule + the specific gap), and *a proposed fix*.
2. **Order by impact** — enforcement gaps and coverage gaps first (they're the "AI is sloppy" risks),
   then orphans/overlap, then description health.
3. **Walk them one at a time.** Present finding #1 with its proposed fix; the developer approves,
   edits, or skips; then #2; and so on. Never dump a giant batch of edits — this matches the plan-first,
   approval-gate rhythm, and it's how a harness gets refined.
4. **Never auto-apply.** `/improve` proposes; the developer approves each change. Applied fixes should
   also get a dated entry in the decision log when they're a real decision.

## The honest boundary
`/improve` finds drift and proposes fixes — it is not a rewrite engine and not a substitute for
judgment. It surfaces "here's where the harness doesn't practice what it preaches," with evidence, and lets the
developer decide. A finding it can't substantiate with a specific rule + specific gap is not a finding —
don't pad the list to look thorough.

## Evals as evidence
When auditing a generative skill, check whether it has `evals/evals.md` and whether the numbers are
current. A skill with no evals is not necessarily broken, but it is **unmeasured** — say so rather than
guessing at its quality. See the `skill-evals` skill for what a case and an honest report look like.

## Rules
- Read-only audit; propose fixes, never auto-apply. One finding at a time, approval-gated.
- Complements `check-content.py` (mechanical) — `/improve` is the judgment layer; don't re-run mechanical checks.
- Every finding needs evidence: the specific rule + the specific place it isn't honored.
- Prioritize enforcement + coverage gaps (the trust risks) first.
- Where session history isn't available, skip the friction check honestly — don't invent patterns.
