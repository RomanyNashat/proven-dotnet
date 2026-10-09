---
name: skill-evals
description: How proven-dotnet evaluates its own generative skills: test cases beside the skill, a subagent per case, with-skill vs without-skill benchmarking, description trigger-rate tests, and honest reporting rules.
---

# Skill Evals — proving a skill actually works

proven-dotnet edits production code and writes documents people trust, so "the skill looks well written" is not
evidence. This defines how a skill is **measured**. Modelled on Anthropic's official `skill-creator`
eval approach, with the reporting discipline borrowed from projects that refuse to flatter their own
numbers.

**Applies to generative skills only** — the ones that produce an artifact (`service-documentation`,
`story-engine`, `handover`, `scaffold-from-sql`, `design-patterns`). Reference skills (`redis-patterns`, `kafka-patterns`) produce no
artifact and need no evals.

## Where evals live — a Level-3 file, zero description cost

```
.claude/skills/<name>/
  SKILL.md            ← the skill (loaded when triggered)
  evals/evals.md      ← the cases (loaded ONLY when running evals)
```

Because it sits beside the skill and is never referenced from the frontmatter, an eval file costs
**nothing** against the skill-listing budget. That is the same progressive-disclosure property that lets
`handover` keep a 240-line template out of its body.

## What a case looks like

A case is a **prompt**, the **conditions that must hold** in the output, and the **traps** — things a
plausible-but-wrong answer would do. Traps are the valuable half; anyone can write a case that passes.

```markdown
### Case: service with no HTTP surface
**Prompt:** Document the OrderCleanupWorker service.
**Setup:** a worker service — hosted service + Quartz job, no controllers.

**Must:**
- no "API" or "Endpoints" section at all
- the background job is described with its schedule
- every config key named appears in appsettings or IOptions

**Must not:**
- invent endpoints (the failure this skill exists to prevent)
- emit an empty "API — none" section as a placeholder
- pad gotchas with generic advice ("be careful with configuration")

**Trap:** the template lists an API section, so the tempting move is to keep the heading and write
"N/A". The skill says drop inapplicable sections — dropping it is the pass.
```

## How a run works

1. **One subagent per case, clean context.** A case must not be graded by the same pass that produced
   it, and cases must not see each other. Fresh context per case or the result is worthless.
2. **Grade against the conditions with evidence** — quote the line that passes or fails. A verdict with
   no quoted evidence is an opinion.
3. **Benchmark with-skill vs without-skill.** Run the same prompts with the skill unavailable. If the
   pass rate is the same, **the skill is not earning its context cost** — that is a real result and
   should be reported, not buried.
4. **Description trigger test.** Write prompts that *should* fire the skill and prompts that *should
   not*, then check what actually fires. This measures the thing that budget-trimming puts at risk:
   a description can be short and still fire correctly, or long and still miss.
5. **A/B when changing a skill.** Run the old and new versions on the same cases before replacing.

## Reporting — the honesty rules

These exist because a flattering eval is worse than no eval: it manufactures confidence.

- **Report the held-out number as the headline.** If cases were used to tune the skill, they no longer
  measure it. Keep a set the skill was never adjusted against and lead with that.
- **Never headline a number reached by inspecting failures.** Looking at what failed and patching the
  skill to pass those specific cases is teaching to the test. If you do it, say so and report the
  pre-tuning number too.
- **No unlike-for-like comparisons.** Don't put a trigger-rate next to an output-quality rate and imply
  they measure the same thing.
- **Report the cost**, not just the score — tokens and time with the skill vs without. A skill that adds
  two points of quality for triple the context is a bad trade.
- **State the sample size.** Six cases is six cases; do not write it as a percentage that implies more.

## The honest limit — what evals cannot do

Same-model evals reliably catch **mechanical** violations: a missing section, a banned term, an
unverifiable claim, a wrong shape. They do **not** reliably judge quality — a model grading its own
prose grades generously.

So: use evals for mechanical conformance, and keep a **separate reviewer** for judgment. That is why
`story-reviewer` is its own agent, and why `doc-review` works — it checks claims against source rather
than assessing its own writing. **If a check can only be answered by an opinion about quality, it does
not belong in an eval file.**

## Rules
- Evals live in `evals/evals.md` beside the skill; they cost nothing against the listing budget.
- Every case carries traps, not just happy paths.
- One subagent per case, clean context, evidence quoted for every verdict.
- Benchmark against no-skill; a skill that changes nothing should be deleted, not defended.
- Lead with held-out numbers; disclose any tuning; report cost alongside score.
- Mechanical conformance only — judgment stays with a separate reviewer.
