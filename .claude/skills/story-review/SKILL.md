---
name: story-review
description: The self-check gate for /stories: scores a generated map against INVEST + the story rules, auto-fixes failures, reports what it caught.
---

# Story Review — the self-check gate for /stories

`/stories` is good at generating. It was not good at checking its own work — the only reviewer was
the developer, who kept giving the same notes. This skill is the reviewer. It runs after generation,
scores the draft, fixes what fails, and shows what it caught, so slop is caught here instead of in
the developer's lap.

This is a guard pointed **inward** — at proven-dotnet's own output. The other review tools
(`/full-review`, `/quality-gate`, `/health-check`, `code-reviewer`) all check the developer's .NET
code. This one checks the story map `/stories` just wrote.

## Where it runs

`/stories` runs three stages in order: **generate → review → present.** This skill is the review
stage, driven by the `story-reviewer` agent. It runs the same way for Direction A (requirements →
stories) and Direction B (code → stories). It never runs in Direction C (that hands off to
`/plan-feature`).

It reads the generated story map and edits its text. It does **not** read or touch the developer's
repo, and it does not change the code in Direction B — only the story map.

---

## The rubric — two layers, every story scored against both

### Layer 1 — INVEST (read for a backend story map)

- **Independent** — the story stands on its own; it isn't really a slice of another story.
- **Negotiable** — the story sentence says the behavior and the value, not the implementation.
- **Valuable** — there's a real "so that"; the benefit isn't "so it works."
- **Estimable** — only checked when points are on; skip it when estimation is off.
- **Small** — one meaningful component (one endpoint / job / consumer / cron / scheduled task),
  not a whole feature crammed into one story and not a single DTO blown up into one.
- **Testable** — the acceptance criteria can actually be verified.

### Layer 2 — the story rules (the teeth — this is where the real value is)

1. **Voice and precision.** Both ban lists from `story-engine/` §1.5 apply: not stiff (no AI tells —
   leverage, utilize, facilitate, robust, seamless, comprehensive, ensure, enable) **and** not loose
   (no figurative / hallway shorthand or borrowed UI metaphors — drill into, one-shot, fan out,
   bubble up, paints / first paint, noisy caller, drown out, carrying / lagging, hides-for-404,
   breadcrumb, badge, tile, card, dashboard paints). Name the real thing in literal words. This is
   the rule the output kept failing — check it hardest.
   Also hard-fail these specific words (they keep coming back): **"synthetic"** (use "test"), **"preamble"**
   (use "shared facts"), **"hardening"** (name the real action), **"emit"/"surface"/"round-trip"**
   (use "request/response", "returns"). And in **simple mode**, the `As a / I want / so that`
   sentence is a fail — simple uses the "Endpoint(s) to give the [actor] the ability to…" shape;
   As-a/I-want is full-mode only.
2. **Real actor — never the system describing itself.** The actor is a person/role (an admin
   operator, a participant) or a *specific named* caller acting on something else (e.g. "the
   Appointment service" calling Mawid). It is **never** a vague system-as-actor: **"As the
   platform / As the system / As the service, I want…" is a FAIL.** A story is something an actor
   *does*, not the system stating an internal property of itself. Two tells to catch:
   - **System-as-actor sentence:** "As the platform, I want every route to require a token" →
     fail. Rewrite from the human's side ("As an admin, I get 401 when my token is missing/expired")
     or, better, demote it (next point).
   - **Cross-cutting convention written up as a story:** auth-required, the pagination/sort/filter
     shape, the response envelope, the error→HTTP map. These are **shared facts**, not stories.
     They belong in a short **Shared facts** block stated once at the top — not as their own story
     with the system as actor. If the draft made them stories, flag it and fold them into that block.
   Also still a fail: a UI or screen as the actor.
3. **Right granularity.** One story per meaningful component. Not DTO/artifact level. Not split per
   endpoint when grouping is right; not grouped when the components are genuinely separate.
4. **Clean story text.** No controller / method / stored-proc / class names in the story sentence or
   its description. That detail lives in the sub-tasks. The Reference line carries the
   endpoint/component identifier only — not the full controller→SP→Dapper chain.
5. **Sub-task hygiene.** Every sub-task starts with `[BE]` (or the chosen title tag) and is grouped by
   real work — not one per artifact, not a fixed template. In **full** mode it is a header + body
   block (title plus a short written description); in **simple** mode one plain line is right.
6. **Acceptance-criteria coverage.** The Gherkin covers the real branches the component handles —
   every status code and guard in the code (Direction B) or every rule elicited (Direction A). A
   missing branch is a gap to fill, not a number to hit.
7. **Bugs flagged, never encoded.** Anything that looks like a bug is in the Flags section, not
   written up as intended behavior.
8. **Rationale attached.** Where an ADR explains the "why," it's attached as a Design Rationale note
   on the matching story, and it names the ADR file.
9. **Brevity / density (simple mode).** In simple mode (the `/stories` default), the prose is tight:
   epic ≤ ~3 lines, story description one line (or none when the title + Reference + scenarios cover
   it and lead with a verb — never "a new endpoint that…"), sub-tasks one short `[BE]` line. Flag and
   cut bloat: explainer paragraphs, prose that restates the Gherkin, padding, the same opener on every
   story. The Gherkin itself is never trimmed — brevity applies to the prose around it. (In full mode
   this dimension is relaxed; depth is allowed.)

---

## Scoring — the pre-fix grade (graded on the RAW draft, before any fix)

Grade the draft as generated, before fixing anything. Seven dimensions, each A–F:

| Dimension | What it measures |
|---|---|
| Voice / Precision | Layer-2 rule 1 — stiff or loose language across all three levels |
| Actor | Layer-2 rule 2 — real actor; FAIL on system-as-actor ("As the platform/system") or a convention written as a story |
| AC Coverage | Layer-2 rule 6 + INVEST Testable — are the real branches covered |
| Granularity | Layer-2 rule 3 + INVEST Small/Independent — right level, one component |
| Sub-task Hygiene | Layer-2 rule 4–5 — `[BE]` header+body, clean story text, tidy Reference line |
| Flags & Rationale | Layer-2 rule 7–8 — bugs flagged, ADRs attached |
| Brevity | Layer-2 rule 9 — simple-mode density: short, verb-led, no padding (relaxed in full mode) |

Per-dimension grade by violation density across the map: **A** none, **B** one or two minor, **C**
several or one structural, **D** many, **F** the dimension is broadly wrong. Overall grade is the
**lowest** dimension, not the average — one broken dimension drags the map, because that's the one
the developer would have had to fix by hand.

The pre-fix grade is the signal worth watching over time. Because the fix stage rewrites everything
to passing, the post-fix grade is always an A and means little on its own. The pre-fix grade
measures how good the *generation* was before the guard cleaned up. If it climbs week over week, the
§1.5 rules are holding at generate-time and the reviewer is doing less. If it stays low while the
output still ships clean, the generator is still sloppy and only the gate is saving it — which is
exactly the thing the developer should be able to see.

---

## Fixing — auto-fix in place, then re-check

For every failed item, rewrite the story map in place. Fix recipes:

- **Loose/stiff wording** → replace the banned term with the literal phrasing from the §1.5
  before/after table ("the dashboard paints" → "so an admin tool can show it in one request";
  "drill into the group" → "list and rank the members of one group"; "frozen snapshot" → "the saved
  snapshot").
- **UI actor** → swap to the person who uses it ("As an admin reporting UI" → "As an admin").
- **Reference line too heavy** → trim to the route + controller action; the SP/Dapper detail is
  already in the sub-tasks.
- **Implementation in the story text** → move the controller/method/SP names down into a sub-task.
- **Granularity** → merge stories that were split per endpoint when grouping is right; split a story
  that bundles several real components.
- **AC gap** → add the missing branch scenario (drawn from the code in Direction B, from the rule in
  Direction A). Never lower the bar to make coverage "pass" — add the case.
- **Bare-bullet sub-task in full mode** → rewrite as a `[BE]` header + body block (simple mode keeps
  one line).
- **Bug encoded as intended** → move it to the Flags section.
- **Missing rationale** → attach the ADR note where the ADR covers that story, named.

After fixing, run the rubric again. Loop until the map passes or until the only items left are ones
that can't be auto-fixed.

**What is NOT auto-fixed** (surface as a question, never silently pass): a story whose value is
genuinely unclear (no real "so that" can be inferred), a suspected bug where flag-vs-fix is the
developer's call, or a granularity split that changes the meaning of the map. Raise these in the
report and let the developer decide.

---

## The report — hybrid (this is what `/stories` shows)

Three parts, in order:

```
Story Review — pre-fix grade: B−
  Voice/Precision C · Actor A · AC Coverage B · Granularity A · Sub-task Hygiene A · Flags A

Fixes applied — 4
  • Story 1  [voice]        "the dashboard paints" → "so an admin tool can show it in one request"
  • Story 1  [reference]    trimmed Reference line to the endpoint + action
  • Story 5  [no-UI]        "a 'leading now' badge" → "the current rank-1 leader"
  • Story 9  [AC gap]       added scenario: group not in this tournament → 404

Needs your call — 1
  • Story 7  granularity: this reads like two components (list + export). Split, or keep as one?

Post-fix: A — all auto-checkable rules pass.
```

- **Pre-fix grade** — overall (lowest dimension) plus the six dimensions, on the raw draft.
- **Fixes applied** — per story, tagged with the rule, showing the change. This is what builds the
  developer's trust that the guard works.
- **Needs your call** — the few items that aren't safe to auto-fix.
- **Post-fix line** — confirmation the map now passes (or the short list of what's still open).

## Plan Mode

In Plan Mode, show the report and the proposed fixes for approval **before** writing files. The
developer reviews, adjusts the "needs your call" items, then says go and the files are written. Out
of Plan Mode, apply the fixes, write the files, and include the report inline above the file links.

## Hard limits

- Score against this checklist, not against a feeling. Every grade traces to a named rule.
- Never water down acceptance criteria to make coverage pass — add the missing branch.
- Never invent story points; respect the estimation setting.
- Never touch the developer's repo or code — only the story-map text.
- Keep the developer's real references and the jargon-with-reference rule intact — translate the
  cryptic name in the prose, but never delete the real endpoint/component reference.

## Honest limit of this gate

This is the same model family grading its own output, not an outside auditor. It holds up because it
scores against concrete, checkable rules — the ban lists, real-actor, AC-branch coverage — closer to
a lint pass than an opinion. Treat the pre-fix grade as a trend line, not a certificate.

## Generalizing later

The pattern here — generate → score on a raw draft → auto-fix → hybrid report — is written to
generalize. The same inward-guard shape can wrap `/handover` (score a handover doc against the
17-section template and the voice rules) and `/scaffold` (score generated service code against the
project's conventions). Built for `/stories` first; the structure is meant to be reused.
