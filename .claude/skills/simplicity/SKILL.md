---
name: simplicity
description: Plain-writing rules for shareable output (story maps, docs, READMEs, emails): simple-vs-full modes, template, verb bank. Ask simple-or-full first.
version: 1.0.0
---

# Simplicity Skill

The job of this skill: anything proven-dotnet writes for **other people to read** must sound like a
developer wrote it — short, plain, human — not like an AI. This is the layer that got Romany's
story map approved after an AI-sounding draft was rejected.

It is shared. `/stories` uses it, and so should any command that produces a shareable artifact
(documentation, README, email, write-up, doc package). It does **not** apply to reader-only
internal reports like `/health-check` and `/full-review` — those are for the developer's eyes and
don't need it.

---

## 1. Two modes — simple and full

Both modes keep the **same structure** (nothing is dropped — epic, stories, sub-tasks, flags,
rationale all stay). The difference is density.

- **simple** — tight and human. Each part is as short as it can be while still complete. One-line
  story descriptions, 2–3 line epics, one-line sub-task notes. No explainer prose, no padding.
  This is what a developer writes by hand.
- **full** — the detailed style: fuller descriptions, header+body sub-task blocks, longer rationale.
  For when the reader genuinely wants the depth.

**When to ask vs default:**
- `/stories` → **simple is the default.** Still mention it ("Generating in simple style — say
  'full' if you want the detailed version").
- **Any other shareable-writing request** ("document this", "write a readme", "draft an email",
  "write up X") → **ask first: "Simple or full?"** Don't assume.
- Reader-only internal output (`/health-check`, `/full-review`) → don't ask, don't apply this skill.

---

## 2. The template (simple mode — the default)

Simple mode does **not** use the `As a / I want / so that` story sentence. That template belongs
to **full mode only**, and only after the mode question is asked. Simple mode uses Romany's shape:
describe the component plainly, third-person, terse.

```
## Epic: [short name]
[2–3 plain lines: what the service does and who uses it. No essay.]

### Story: [what it does, plainly — verb-led title]
[ONE line in the shape: "Endpoint(s) to give the [actor] the ability to [verb] …" — or lead
 straight with the verb ("Returns …", "Lists …", "Ranks …"). Third-person (the Admin / the
 caller), NOT "As a … I want …". Skip this line entirely when the title + Reference + scenarios
 already say it.]

Reference: `[METHOD /route]`
[At most one extra line, and only for a real rule the scenarios don't show — e.g.
 "Invited = status 4 (Pending)". Otherwise nothing.]

```gherkin
[scenarios — UNCHANGED, full coverage of the real cases]
```
```

Examples of the simple description line (Romany's voice):
- "Endpoints to give the Admin the ability to create, read, edit, and archive campaigns."
- "Returns a challenge's full report in one call."
- "Ranks the members inside one group against each other."

**Full mode** (only when asked): may use the `As a [role], I want [x], so that [y]` sentence plus a
fuller description. The mode question offers this; simple never uses it.

The Gherkin is never simplified or trimmed in either mode. Simplicity applies to the prose around
it, not the scenarios.

---

## 3. The verb bank — use these, avoid those

Seeded from Romany's own approved writing. Lead with the left column; never reach for the right.

| Use (plain — Romany's voice) | Avoid (AI tells) |
|------------------------------|------------------|
| returns, gives, reads, lists, ranks, fetches, shows, sends | surfaces, exposes, emits, leverages, facilitates, provides |
| request / response, returns a response | round-trip, emit, responds with a payload |
| reads from, runs through (a stored procedure) | is backed by, is powered by, orchestrates |
| limit / check / protect / block (name the real action) | hardening, harden transport, operational hardening |
| in one call, paginated, by status, per caller | seamlessly, in a single round-trip, comprehensively, holistically |
| returns 400 / 404 / 204 | rejects with a, responds with an error of |
| handles, checks, rejects | ensures, guarantees, enforces, validates that |

**Hard rules (Romany's explicit asks):**
1. **Use "request" / "response".** The API speaks in requests and responses. Never "emit",
   "round-trip", or "surface".
2. **Never "hardening".** If you'd write "hardening", instead name what you actually do —
   "rate-limit per caller, set strict response headers, return safe errors that don't leak SQL,
   and fail fast at startup" — and only mention it if it's needed at all.
3. **Never "synthetic".** Use "test" (a test user, a test context), not "synthetic".
4. **Never "preamble".** Use "shared facts" (or just "shared") for the up-front conventions block.

Also avoid jargon-as-noun in general: "surface", "hardening", "synthetic", "preamble", "the funnel"
(translate it). If a word is a noun that's really a piece of jargon, swap it for the plain thing it
means.

---

## 4. Do / Don't — real pairs

From the rejected AI draft vs Romany's approved file. Copy the right column's habits.

| ❌ Don't (AI-bloat) | ✅ Do (Romany's style) |
|---------------------|------------------------|
| "This composite reads one stored procedure that emits meta, funnel, performance, and the rank-1 leader in a single round-trip. Performance and the current leader are only filled in when the challenge actually has step data…" | "Returns a challenge's full report in one call." |
| "A read-only reporting **surface** over the Steps_DB challenge data." | "A read-only reporting **service** that reads from Steps_DB." |
| "The service adds the operational **hardening** a gateway-fronted PII endpoint needs…" | "Behind the gateway it limits requests per caller, sets strict response headers, and returns safe errors that don't leak SQL." |
| Story description running 4–6 lines | Story description = one line (or none) |
| "…**emits** meta, funnel, performance…" | "…**returns** meta, funnel, and performance…" |
| Restating in prose what the Gherkin already says | Let the scenarios carry it — cut the prose |
| "a new endpoint that does X" (every story opens the same) | Vary it, lead with the verb: "Returns…", "Lists…", "Ranks…" |

---

## 5. Tone — what "sounds like Romany"

- Short. One idea per line. If a sentence is doing two jobs, split or cut.
- Lead with the verb and name the real thing (the route, the table, the status).
- No selling, no hedging, no AI throat-clearing ("This endpoint is designed to…").
- Plain over clever, every time.
- It's fine — often better — to say less. A title + reference + Gherkin with no description is a
  perfectly good story.

---

## 6. Applies beyond stories

For documentation, READMEs, emails, and write-ups, the same rules hold: lead with the point, use
the verb bank, keep it short, drop the padding. A README section is a few plain lines, not an
essay. An email says the thing and stops. The template in §2 is story-shaped, but §3–§5 (verb
bank, do/don't, tone) apply to everything shareable.
