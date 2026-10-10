# Eval results

Held out: neither the commands nor their agents were changed before these runs. One run each, so these
are three results, not rates.

## 2026-10-10 — first runs (proven-dotnet 0.1.18)

Each run had its own copy of `PharmacyRefills` in a git repo outside this one, and was told not to read
`tests/`. NuGet was unreachable in all three (the restore got a 403), so no `dotnet list package` and no
build ran.

| | `/full-review` with proven | Plain review (baseline) | `/health-check` with proven |
|---|---|---|---|
| How it ran | One subagent following `full-review.md` and the three agent definitions. It had no tool to launch the agents, so it did their work itself. | One subagent, "review this before merge", told nothing about proven. Started inside the repo, so it **had the house rules** (it cited the bounded-string rule and the zero-vulns gate). Rules only, no commands, agents or skills. | The `health-analyst` agent with `code-health` |
| Planted findings | **12/12** | **12/12** | Not graded by count (the cases ask about the grade) |
| Severities within one level | 12/12 (F7 Critical, expected Warning: one level) | 12/12 (F8 Suggestion, expected Warning: one level) | — |
| Distractors wrongly flagged | 0/3 (D1 listed as a positive) | 0/3 (D1 listed as a positive) | D1 not flagged |
| Not planted, but real | R1 found; R2 missed | R1 and R2 found | — |
| F9 without the tool | Said the scan couldn't run, read the project file, named CVE-2024-43485, GHSA-8g4q-xg66-9fp4 and 8.0.5 | The same | **Named it but left it out of the Security score** ("not confirmed by a tool"). Case 2 fails on this. |
| Fixture unchanged | Pass | Pass | Pass |
| Tokens / time | 153k / 4.9 min | 128k / 3.9 min | 82k / 3.5 min |

`/health-check`: F (35/100) on 4 of 8 dimensions; the other 4 marked N/A with the reason, not invented.
Coverage scored 0 because no test project exists, and said so. The path to the next grade led with the
injection, the IDORs and tests. Cases 1, 3 and 4 pass; case 2 fails on F9.

## What it means

- **The fixture doesn't separate the two.** A strong model with the house rules finds all twelve planted
  findings without the commands, agents or skills. Equal scores mean either the review agents aren't
  earning their context, or the fixture is too easy. The planted findings are textbook ones, so the
  fixture is the first suspect. Next: findings that need the skills to see (the ones in the October story
  tests: a singleton holding a scoped service that only fails in Production, a Quartz trigger without
  `ScheduleTriggerRelativeToReplacedTrigger`, OpenIddict 6 on EF Core 10, a base address without the
  trailing `/`, a PHI audit written before the save), and a baseline run from outside the repo.
- **What proven added here:** house-rule findings the baseline didn't make (identity column not
  `GENERATED ALWAYS`, the trigram index as a reviewed script, the outbox skill by name), at about 20% more
  tokens. The baseline caught one real defect proven missed (R2, the missing JwtBearer package).
- **The fan-out was not tested.** A subagent can't launch agents, so `/full-review`'s three parallel
  reviewers didn't run. `skill-evals` now says how to run a command that launches agents.

## Fixed after these runs

- `code-health` 1.1.0: when the package scan can't run, the direct references are checked against
  published advisories and a confirmed match counts in Security. Not re-run yet: per `skill-evals`, a
  number reached by fixing a failure isn't the headline.
- `skill-evals`: running commands that launch agents, and what "without" means for a run started inside
  the repo.
