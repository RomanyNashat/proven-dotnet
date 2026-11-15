# Keeping every skill findable as the harness grows

**Date:** 2026-11-15
**Service:** proven-dotnet (the skill listing)
**Status:** in-progress

## Decision
**The commands stay listed.** The bake-off's winner was to hide the 20 model-invocable commands, but it
rested on one fact it couldn't check: how often a developer starts a command from plain words. The answer
(2026-11-15) is often. Hiding them would break a habit the team relies on, and keeping the relied-on ones
visible would eat the room it frees, so that design buys nothing here.

What happens instead:
- **The hub design comes back, but only after an eval** shows its extra hop doesn't lose routing: requests
  sent to a listing of hubs, scored on whether the model opens the right member skill, against today's flat
  listing. Until that eval passes, nothing moves.
- **Until then**, a new skill only some services need goes to `project-skills/` (not installed, no listing
  cost), and the routing check and the budget check stay as they are: a new listed skill has to make its
  own room by shortening others, with the routing check proving nothing got worse.
- `check-listing-budget.py` prints the headroom for a 1M and a 200K window (taken from the winner), so the
  squeeze shows before it fails.

## Alternatives Considered
Three designs were written apart, each with a different lens and none told of the others, and scored by a
judge who wrote none of them (`architect`'s bake-off), against criteria and weights written first:

| Criterion | Weight | Hide all commands | Hide commands, with a visible list | Hub skills |
|---|---|---|---|---|
| Requests still reach the right skill | 5 | 3 | 4 | 2 |
| Fits the budget as the backlog lands, with room for a layer and plugins | 5 | 3 | 3 | 5 |
| Upkeep for each new skill | 3 | 4 | 4 | 2 |
| What a developer has to change | 3 | 2 | 3 | 3 |
| Context each session pays beyond the listing | 2 | 5 | 5 | 3 |

`tradeoff.py` on the judge's scores, before the deciding fact was in: the visible list 73.3%, hide all 64.4%, hubs 62.2%, **clear** (no one-point score or weight
move of 2 or less changes it; hubs only tie with routing weighted 0 or the budget 10).

- **Hide all commands, no exceptions:** the same room, but every prose habit across 20 commands becomes an
  X version with no way to keep one.
- **Hub skills:** about nine listed hubs (data access, messaging, testing...) whose tables point to hidden
  member skills: the listing falls to about 4,500 characters and new skills cost nothing in it. Rejected
  for now: every request takes a hop through a hub that no check proves the model follows, Claude Code no
  longer matches individual skills, and 35 references to skills by path or name would need checking. Kept
  as the next step when the room runs out.

## Reasoning
- **The judge checked the designs' facts in the repo:** commands take 2,165 characters and skills 9,825;
  exactly 20 commands are model-invocable and seven already hidden; the team layer's three commands are
  hidden (core plus layer 14,863 of 16,000); nothing calls a skill through the Skill tool. One claim was
  overstated: `retrospective` asks a side-effect command for the flag **or** the never-self-commit guard,
  so `/commit` isn't a gap; `/migrate` and `/docker-build` have neither.
- **Routing is unchanged:** `check-skill-routing.py` already ranks skills only.
- **The judge named the fact that would change its verdict**, and it did: with commands started from plain
  words, the winner's score for "requests still reach the right skill" and for what a developer has to
  change no longer holds. A clear table can still rest on one unchecked fact; the bake-off asks for it.
- **Hiding commands would have bought about 14 skills of room, not a fix.** The hubs are the only design
  here that lasts, which is why they get the eval.

## How the bake-off itself went
Two of the three lenses (the simplest, the cheapest to run) reached nearly the same design; only the
"holds up when things fail" lens produced a different one. A lens alone doesn't guarantee three different
designs: the next bake-off should say what each must differ in.
