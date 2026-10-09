---
name: story-engine
description: Generate epics/stories/sub-tasks from requirements or code: INVEST, Gherkin AC, Jira hierarchy, Markdown + CSV, Jira push.
version: 1.1.0
---

# Story Engine Skill

This skill is the knowledge core for turning work into structured agile artifacts — in both
directions:

- **Requirements → Stories** — a developer/PO describes a feature; produce epics, stories, tasks
- **Code → Stories** — analyze an existing service; reverse-engineer what it does into epics,
  stories, and tasks, with ADRs layered in as design rationale
- **Stories → Code** — take approved stories and hand them to the build pipeline
  (`/plan-feature` → `/tdd` → `/full-review`)

It powers the `/stories` command via two agents: **business-analyst** (requirements-driven) and
**code-analyst** (code-driven). The command picks the agent based on the chosen direction.

**Design principle (locked):** When generating from code, **the code drives the structure** —
the epic/story/task hierarchy mirrors the actual implementation. **ADRs drive the rationale** —
they answer "why this design, why this choice over the alternative" and attach as a Design Rationale
note on the relevant story. ADRs never define the hierarchy; they enrich it.

---

## 1. Story Format Standards

### 1.1 User Story Template

In **full** mode, every story uses the canonical form (simple mode drops it, see §1.5):

```
As a [role/persona],
I want [capability],
so that [benefit/business value].
```

The "so that" is mandatory in full mode — a story without a benefit is a task, not a story. If the benefit
can't be articulated, question whether the story belongs.

### 1.2 INVEST Quality Criteria

Every story should satisfy INVEST. Use this as the quality gate before finalizing:

| Letter | Criterion | Check |
|--------|-----------|-------|
| **I** | Independent | Can be built without depending on another story being done first (minimize coupling) |
| **N** | Negotiable | Describes intent, not a rigid spec — leaves room for implementation choices |
| **V** | Valuable | Delivers visible value to a user or stakeholder |
| **E** | Estimable | Team can size it; if not, it's too vague or too big |
| **S** | Small | Fits in a sprint; if not, split it |
| **T** | Testable | Has clear, verifiable acceptance criteria |

If a story fails a criterion, fix it: split if too big (S), clarify if not estimable (E), add
acceptance criteria if not testable (T), etc.

### 1.3 Acceptance Criteria — Gherkin

Acceptance criteria use Given/When/Then (BDD). **Cover the component's real cases** — the happy
path plus every meaningful branch the code actually handles: success, each error/status code
(400/403/404/409/204…), edge cases (empty page, out-of-range, deleted/missing), and important
variations (live vs ended, group vs individual). Write as many scenarios as the behavior needs —
a simple endpoint may have 2, a stateful one may have 6+. Don't pad with trivial duplicates, but
never drop a real case to hit a number. These scenarios are the part the team relies on most;
keep them precise and testable.

```gherkin
Scenario: [short description]
  Given [initial context / precondition]
  When [action or event]
  Then [expected outcome]
  And [additional outcome, if needed]
```

Keep each scenario to a single behavior. Exhaustive low-level permutations still belong in the test suite, but the
story's scenarios should name every real case a reader needs to know.

### 1.4 Definition of Ready / Done

- **Ready** (before work starts): story follows the template, has scenarios covering its real cases, passes INVEST,
  is sized (if estimation is on), and dependencies are noted.
- **Done** (after work completes): code merged, AC met, tests passing at/above the coverage
  thresholds, reviewed via `/full-review`.

### 1.5 Writing Style — Plain, Precise, and Human (epics, stories, AND sub-tasks)

**Use the shared `skills/simplicity/` layer** — it holds the verb bank (use "returns/lists/ranks",
avoid "emit/surface/hardening"; say "request/response"), the do/don't pairs, and the simple-vs-full
modes. `/stories` defaults to **simple** mode. Everything below refines it for story maps.

**Simple mode shape (the default):**
- **No `As a / I want / so that` sentence in simple mode.** That template is **full mode only**,
  and only after the mode question is asked. Simple mode uses this shape: "Endpoint(s) to give
  the [actor] the ability to [verb]…", third-person (the Admin / the caller), or lead straight with
  the verb ("Returns…", "Lists…", "Ranks…"). Keep it to **one line**, and **drop it entirely** when
  the title + Reference + scenarios already say it.
- **Epic** is 2–3 plain lines, no essay.
- **Sub-tasks** are one short plain line each in simple mode (still `[BE]`); the header+body block
  form is **full** mode.
- **Never "synthetic" or "preamble"** — use "test" and "shared facts". (See `skills/simplicity/`.)
- The **Gherkin is never simplified** — full scenario coverage in both modes.

Examples (verb-led, simple):
- ✅ "Returns a tournament's full report in one call."
- ✅ "Lists the participants with their contact details, paginated."
- ✅ "Ranks the members inside one group against each other."
- ❌ "a new endpoint that surfaces a comprehensive view…" (AI tell + same opener every time)

Everything you write — epic descriptions, stories, scenarios, and sub-tasks — should read like a
**clear ticket a teammate could pick up cold**: plain and human, but exact. Like a good team lead
wrote it, not like an AI — and not like loose hallway slang either. This is a hard requirement, not a
preference, and it applies to **all three levels**.

The target register is a senior dev writing a precise ticket: conversational, but every word
means one thing. There are **two ways to fail it**, and you must avoid both:
1. **Too stiff (AI/corporate).** Sounds like a machine.
2. **Too loose (slang/figurative).** Sounds casual but says nothing exact — vague, ambiguous,
   or borrowing UI metaphors a backend story has no business assuming.

**Guardrail 1 — Not stiff (no AI tells):**
- Write conversationally. Short sentences. One idea each.
- No AI/corporate words: avoid "leverage, utilize, facilitate, robust, seamless, comprehensive,
  holistic, paradigm, synergy, streamline, ensure, enable." Use the plain version (use, help,
  strong, smooth, make sure, let).
- No flowery phrasing, no grand "deep quote" statements, no marketing words ("converted",
  "regardless of state", "page-numbered", "frozen snapshot").

**Guardrail 2 — Not loose (plain ≠ vague — be exact):**
- **Name the real thing.** Say what the component actually returns or does, in literal terms —
  not a figure of speech. "Returns the whole summary in one call," not "the dashboard paints."
- **No figurative / hallway shorthand.** These read human but mean nothing precise — banned:
  *drill into, drill-down, one-shot, fan out, fans out, bubble up, paints / first paint, noisy
  caller, drown out, loud at startup, carrying the group, lagging, hides (for a 404).* Replace
  each with the literal action (see the table below).
- **Don't presume a UI.** A backend reporting/data story describes the **data and behavior** the
  component produces. The actor can be an admin, but the *benefit* must not lean on a specific
  frontend widget — **no "breadcrumb", "badge", "tile", "card", "screen paints", "first paint"** —
  unless the code itself dictates it. If you must point at a consumer, say "an admin tool" /
  "an admin screen" generically; better, describe the data ("so a header view has the basics
  without pulling the whole report").
- **No ambiguous nouns.** "header" alone is ambiguous (HTTP header? UI header?) — say "the summary
  payload" or "the tournament's basics". Use "round-trip" sparingly and only literally ("one
  request instead of four").
- If a word isn't doing exact work, cut it. Don't pad the benefit, and don't dress it up.

**Real actors only (stories):**
- The actor is a real person/role ("As an admin", "As an operator", "As a participant") or a
  *specific named* caller acting on something else ("As the Appointment service" calling Mawid).
- **Never a UI or a screen** — "As an admin reporting UI" is wrong; a UI is not an actor.
- **Never the system describing itself** — "As the platform / As the system / As the service, I
  want every route to require a token" is wrong. That's the system stating its own internal rule,
  not an actor doing something. Rewrite from the human's side ("As an admin, I get 401 when my
  token is missing or expired"), or demote it (next point).
- **Cross-cutting conventions are shared facts, not stories.** Auth-required, the pagination/sort/
  filter shape, the response envelope, the error→HTTP map — state these ONCE in a short
  **Shared facts** block at the top of the map, not as their own `As a…` stories. Don't manufacture a system-actor story
  just to house a convention.

**Keep implementation OUT of the story sentence:**
- The story says *what* the user gets and *why* — in behavior terms. No controller names, method
  names, stored-proc names, or class names in the story body. Those belong in the sub-tasks.
- A `Code:` / "calls X → Y → sp_Z" line does not go in the story. Put that detail in a sub-task.

**Before/after — story wording (stiff AND loose both fixed):**

- ❌ *Stiff:* "As an admin reporting UI, I want counts of joined / withdrawn / invited / viewers
  plus an acceptance rate, so I can show how participation converted regardless of tournament state."
- ✅ "As an admin, I want to see how many people joined, withdrew, were invited, or only viewed a
  tournament — plus the acceptance rate — so I can see how take-up went at any stage."

**Before/after — loose/figurative wording (the trap this codebase fell into):**

| ❌ Loose / figurative / UI-borrowed | ✅ Plain and exact |
|---|---|
| "the whole header in one call so the dashboard paints" | "the whole summary in one call so an admin tool can show it without four separate requests" |
| "a one-shot report header" | "the full tournament summary in a single request" |
| "so a header or breadcrumb can show context" | "so an admin screen can show the tournament's basics without pulling the whole report" |
| "a 'leading now' badge" | "the current rank-1 leader" |
| "drill into one group's members" | "list and rank the members of one group" |
| "the procedure fans out to the atomic procedures" | "the procedure calls the smaller per-section procedures and returns their results together" |
| "results bubble up in a single round-trip" | "all the result sets come back from one database call" |
| "who is carrying the group and who is lagging" | "which members have the most points and which have the fewest" |
| "one noisy caller can't drown out the rest" / "loud at startup" | "one heavy caller can't use up everyone else's rate limit" / "logs a clear warning at startup" |
| "the tournament is hidden the moment it's missing" | "a missing or deleted tournament returns 404" |

Plain beats clever, and exact beats either — at every level. Conversational, never vague.

---

## 2. The Hierarchy — Epic → Story → Sub-task

Jira (and standard agile) enforces a specific three-level hierarchy. Getting this right is
critical for the CSV export to import correctly.

```
Epic                          ← large body of work / a feature or capability
 └── Story                    ← user-facing increment of value (INVEST)
      └── Sub-task            ← technical step inside a story
```

**Critical Jira constraint:** Stories and Tasks sit at the SAME level — you cannot nest a Task
directly under a Story. The valid three-level nesting is **Epic → Story → Sub-task**. When you
need technical steps under a story, they are **Sub-tasks**, not Tasks.

### 2.0 Up-Front Questions (ask these first, before generating)

Ask all three at the start, together — they shape the whole output:

1. **How many epics?** "How many epics should this be? (1, 2, 3, or more — or I can decide.)"
   - Given a number → produce exactly that many epics and group the stories under them (split by
     capability, service, or user journey to fit the count).
   - "You decide" → pick a sensible number (usually one epic per cohesive capability/service) and
     say what you chose in one line.

2. **How to split into stories?** "How should I split the work into stories — one story per
   component (each endpoint / background job / consumer / cron job), or group related ones into
   fewer stories?"
   - Default and recommended: **one story per component** (see §2.1 — a component is the unit, not
     an endpoint specifically).
   - If they say "group," combine closely related components into fewer stories and note how you
     grouped them.

3. **Title tag?** "Want a tag on the titles? Default `[BE]` for backend — or none, or a custom
   tag." Apply the chosen tag to epic and story titles. (Sub-tasks ALWAYS get `[BE]` regardless —
   see §2.1.)

**Style mode:** `/stories` runs in **simple** mode by default (the plain, tight style — see §1.5
and `skills/simplicity/`). Note it to the developer ("Generating in simple style — say 'full' for
the detailed version"); switch to **full** only if they ask. Don't make it a question — simple is
the default for stories.

These questions run in both directions (requirements → stories and code → stories).

### 2.1 What Is an Epic vs Story vs Sub-task

| Level | Represents | Code mapping (code → stories) | Example |
|-------|-----------|-------------------------------|---------|
| **Epic** | A feature, capability, or bounded context | A feature folder / service / module | "Notification Delivery" |
| **Story** | One meaningful component the system runs | An endpoint, background job, consumer, cron/console job | "As a participant, I get a push notification when a tournament completes" |
| **Sub-task** | A technical step to deliver the story | A class/method/integration/SP the story needs | "[BE] Add Firebase batch-chunking to NotificationConsumer" |

**Epic — give it a real description, not just a title.** The epic describes everything underneath
it: a short, plain summary of what all its stories add up to (the whole capability), in human
voice. Two or three sentences that tell a reader what this group of work delivers. Not a bare
title like "Notification Delivery" — say what the stories beneath it actually give people.

**Story — one per meaningful COMPONENT, written at the behavior level.** A story is not strictly
"one endpoint." The unit is whatever meaningful thing the system does:
- an API endpoint
- a background job / worker
- a console or cron job
- a Kafka consumer / message handler
- a scheduled task

Write the story by **what that component does for the user or the business** — the behavior and
why it matters. A nightly leaderboard-recalc job is "the standings stay up to date overnight," not
a list of DTOs. **Never drop to artifact-level value statements** like "this DTO helps the user
because…" — that's wrong. Stay at the component level.

Each story has: in full mode a plain `As a / I want / so that` sentence, in simple mode one verb-led
line (§1.5), either way with a real actor (a role or named service, never a UI); a short behavior
description (NO controller/method/SP/DTO names in it); and its acceptance-criteria scenarios (§1.3).

**Translate jargon, keep the reference.** When the code uses an internal or cryptic name — a route
word like `funnel`, a terse method or job name — write the story text in plain words that say what
it actually does, but **keep the real endpoint/component identifier** as a reference (in the
endpoint line and the sub-tasks). Translate the prose; never delete the real name.
- ❌ "As a tournament admin, I want the funnel…"
- ✅ "As a tournament admin, I want to see how participation broke down — joined, withdrew, invited,
  viewers — so I can see where people dropped off." *(endpoint `GET /…/funnel` still referenced)*

**Sub-task — `[BE]`, header + body, shaped to the real work.** Every sub-task:
- **Starts with `[BE]`** — always (backend sub-task), regardless of the story title tag.
- **Full mode: a header + body block, not a bare bullet.** A title line, then a short written
  description of the work — even two lines, but a real paragraph with a heading. **Simple mode: one
  plain line**, still starting with `[BE]` (§1.5).
- **Carries the technical detail** the story doesn't — the controller/service/repo/SP/DTO work,
  which layer, what to add or change.
- **Is shaped to the actual work, NOT a fixed template.** Group thin layers together (service +
  repo as one), split out anything substantial (a non-trivial SP, a tricky integration). Don't
  make every DTO its own sub-task. Let the work decide the breakdown and the count.

Example sub-task (header + body, one valid shaping — not a template):

> **[BE] Add the membership endpoint and service**
> Add `POST /api/groups/{id}/members`. It takes the user id, calls `AddUserToGroupAsync`, and
> returns the result — mapping group full → 409, no permission → 403, group/user missing → 404.
> The service checks the group exists, isn't full, and the user isn't already in it before the
> insert. Repo + Dapper detail lives in the next sub-task.

### 2.2 Splitting Heuristics

- An epic that would take more than ~2 sprints → consider splitting into multiple epics
- A story that can't be demoed in one increment → split (by workflow step, by CRUD operation,
  by happy-path vs edge-case, by data variation)
- Sub-tasks: shape them to the endpoint's real work, not a fixed count. Split when a step is big
  enough to stand alone (e.g. a non-trivial stored procedure, a tricky integration); combine thin
  layers (service + repo) when they're naturally one piece of work.

---

## 3. Direction A — Requirements → Stories

Driven by the **business-analyst** agent. The developer or PO describes a feature; you elicit
detail and produce the hierarchy.

### 3.1 Elicitation

Before generating, ask only what's needed to write good stories (one round, focused):
- **Personas/roles**: who uses this? (drives the "As a ___")
- **Goal/benefit**: what value, for whom? (drives the "so that ___")
- **Scope boundaries**: what's explicitly in and out?
- **Constraints**: compliance, performance, existing systems to integrate with

Don't over-interrogate. If the developer gave a detailed prompt, infer and state assumptions
inline rather than asking.

### 3.2 Generation

1. Identify the epic(s) — the capability/feature being requested
2. Decompose into stories — each a user-facing slice of value, INVEST-compliant
3. For each story, write Gherkin acceptance criteria covering its real cases (see §1.3)
4. Break each story into sub-tasks — the technical steps (kept implementation-light; the build
   pipeline fleshes these out)
5. Note dependencies and (if estimation on) story points
6. Run the INVEST gate over every story; fix violations

### 3.3 Hand-off to Build

Requirements-driven stories often feed straight into building. The stories → code path (§5)
hands the approved stories to `/plan-feature`.

---

## 4. Direction B — Code → Stories (with ADR Rationale)

Driven by the **code-analyst** agent. This is the reverse-engineering path. **Code drives
structure; ADRs drive rationale.**

### 4.1 What to Read

Three inputs, in priority order:
1. **Code (primary)** — defines the hierarchy. Walk the selected service(s).
2. **ADRs (rationale)** — `docs/decisions/*.md` (or wherever the repo keeps them). Supply the "why".
3. **Git history (optional context)** — recent commits for recency/activity signal. Only if the
   developer opts in.

Service selection reuses the handover scoped directory-picker (see `skills/handover/` §1.5.1):
ask for a source path → list subdirectories → multi-select → "another path?" loop.

### 4.2 Hierarchical Extraction (code → hierarchy)

Walk the code from coarse to fine and map each level:

| Code element | Becomes | How |
|--------------|---------|-----|
| Feature folder / bounded context / service | **Epic** | One epic per cohesive capability |
| A meaningful component: endpoint, background job, consumer, cron/console job, scheduled task | **Story** | Phrase as what the component does for the user/business |
| Class / method / repo / SP / integration that implements the story | **Sub-task** | The technical pieces the story is built from (`[BE]`, header + body) |

Extraction method (proven approach — analyze hierarchically, not line-by-line):
1. **Module/service level** → identify epics (what capabilities does this service provide?)
2. **Component level** → identify stories. A component is any meaningful unit the system runs: an
   endpoint, a background job/worker, a Kafka consumer, a cron/console job, a scheduled task. One
   story per component (unless the developer chose "group" in the up-front question). Describe what
   it does for the user/business — never at DTO/artifact level.
3. **Class/method/repo/SP level** → identify sub-tasks (what technical steps deliver each story).

For each story, reconstruct the form by inferring the persona and benefit from:
- The component's trigger + effect (endpoint route + verb, job schedule, consumer topic) →
  e.g. `GET /api/reports/{id}/participants` → "look up who joined a tournament"; a nightly recalc
  job → "keep the standings up to date overnight"
- Auth attributes / roles (who is allowed → the persona — a real role or named service, never a UI)
- Method/handler/job names and XML doc comments (intent)
- DTO shapes (what data comes back — informs the description, NOT a per-DTO story)

**Translate jargon in the story, keep the real reference.** If the component's name is cryptic
(e.g. a `funnel` route), write the story prose in plain words but keep the real route/job name in
the reference line and sub-tasks (see §2.1).

**Keep the implementation detail in the sub-tasks, not the story.** The controller/method/
stored-proc chain you read from the code (e.g. "GetFunnel → GetFunnelAsync → sp_GetTournamentFunnel")
must NOT appear in the story sentence OR its description paragraph. It goes into the sub-tasks for
that component. The story
stays plain behavior; the sub-tasks carry the code detail.

**Shape the sub-tasks to the endpoint's real build, not a fixed template.** From the code, derive
the technical steps it actually took (or would take) to deliver this endpoint — endpoint method,
service logic, repo/SP, tests — and group them the way the work naturally falls (see §2.1). Each
sub-task gets a short, human description about the code. Don't force the same step list on every
endpoint.

### 4.3 Acceptance Criteria from Code

Derive Gherkin AC from what the code actually enforces:
- Validation rules → Given invalid input, Then rejected with [error]
- Happy path → Given valid request, When called, Then [observed response]
- Branch/guard clauses → the edge cases the code already handles
- Existing tests (if any) → the strongest source of real, intended behavior

These are **characterization-style** criteria — they describe what the code does today. If
something looks like a bug, flag it (don't encode it as intended). This mirrors the
`skills/test-coverage/` characterization principle.

### 4.4 ADR Rationale Layer

For each ADR found, attach its reasoning to the story/epic it relates to (match by service +
subject):

```
Story: As a participant, I get a push notification when a tournament I'm in completes ...

  📐 Design Rationale (from ADR kafka-migration.md):
  Chose Kafka over RabbitMQ — existing Kafka infra, natural retry via consumer offsets,
  decouples API latency from provider latency. Trade-off: added consumer/DLQ complexity.
```

This is the unique value of code → stories: the stories don't just say *what* the
system does, they carry *why* it was built that way. An ADR with no matching code element becomes
a note on the parent epic (a decision not yet reflected in code).

### 4.5 Uses of Code → Stories

- Documenting a legacy/undocumented service as a story backlog
- Migration planning (what exists, so you know what to rebuild)
- Validation — does the built system match the intended stories? (divergence detection)
- Onboarding — a story-level map of an unfamiliar service

---

## 5. Direction C — Stories → Code

Take approved stories and route them into the existing build pipeline rather than
reinventing planning.

1. For each story, hand the story + its Gherkin AC to **`/plan-feature`** (the planner agent),
   which decomposes it into ordered, agent-assigned tasks
2. Implementation flows through **`/tdd`** (test-first for the NEW code)
3. Review via **`/full-review`**, verify via **`/verify`**

Note the clean split: stories → code uses **TDD** (new code, test-first). This is the opposite
of code → stories' characterization approach. The story engine produces the stories; the build
pipeline builds them.

The known LLM weakness here is jumping straight to "build everything at once". Always go through
`/plan-feature` so work is sequenced into a logical, incremental order — that's exactly what the
planner agent exists to prevent.

---

## 6. Estimation (Optional — Default Off)

Story points are **optional** and **off by default**. Ask once: "Add story point estimates?
(Fibonacci 1/2/3/5/8/13 — default: no)".

If on:
- Use the Fibonacci scale: 1, 2, 3, 5, 8, 13 (and 21 for "too big — split it")
- Estimate relative effort/complexity/uncertainty, not hours
- A story at 13 is a signal to split before committing
- Put the value in the story's points field (and the CSV "Story Points" column)

If off: leave the field blank everywhere. Never invent points the developer didn't ask for.

---

## 7. Export Formats

Three formats. The developer chooses one or more. **Markdown and Jira CSV always available; Jira MCP
direct-push is offered when an Atlassian/Jira MCP connector is detected (§7.3).**

### 7.1 Markdown

Human-readable hierarchy tree. Filename: `stories.md`.

```markdown
# [Feature / Service] — Story Map

## Epic: Add users to groups
Everything about putting a user into a group and keeping that membership correct: adding a
member, blocking it when the group is full or the caller has no permission, and making sure a
user can't be added twice. Covers the endpoint, the rules behind it, and the tests.

### Story: Add a user to a group
**As an** admin, **I want** to add a user to a group, **so that** they can take part in the
group's tournaments.
**Points:** 3   (omit line if estimation off)

**Acceptance Criteria**
```gherkin
Scenario: User is added to a group that has room
  Given a group that isn't full and a user who isn't in it
  When I POST /api/groups/{id}/members with the user id
  Then the user is added and the response is 200

Scenario: Group is full
  Given a group that is already at its member limit
  When I POST /api/groups/{id}/members
  Then nothing is added and the response is 409

Scenario: Caller doesn't have permission
  Given a caller who isn't allowed to manage this group
  When I POST /api/groups/{id}/members
  Then the response is 403

Scenario: Group or user doesn't exist
  When I POST /api/groups/{id}/members for a missing group or user
  Then the response is 404
```

**Sub-tasks**  (shaped to this work — not a fixed template; each is `[BE]`, header + body)

**[BE] Add the membership endpoint and service**
Add `POST /api/groups/{id}/members`. It takes the user id, calls `AddUserToGroupAsync`, and returns
the result — mapping group full → 409, no permission → 403, group/user missing → 404. The service
checks the group exists, isn't full, and the user isn't already in it before handing off to the repo.

**[BE] Add the repo and stored procedure**
Add the repo method that inserts the membership row through Dapper, and the guard/insert stored
procedure it calls. The SP does the full/duplicate checks in one place so the rule lives in a single
spot.

**[BE] Add the tests**
Unit-test the service rules (full group, duplicate member, no permission) and add one integration
test that hits the endpoint end to end and checks the status codes above.

**Design note** (ADR group-membership.md): membership goes through a stored proc so the full/
duplicate checks happen in one place. (Only include when an ADR applies.)
```

Notes on the example above: the story sentence and its description are plain behavior with a real
actor (no controller/method/SP names); the scenarios cover the real cases; every sub-task starts
with `[BE]` and is a header + body block (not a bare bullet); and the sub-tasks are grouped by real
work (service folded in with the endpoint, repo with the SP) rather than one per code artifact.

### 7.2 Jira CSV

A CSV that imports cleanly into Jira via the external-system importer (which supports
multi-level hierarchy; the simple bulk-create importer does NOT). Filename: `stories-jira.csv`.

**Required columns** for hierarchy to import correctly:

| Column | Purpose |
|--------|---------|
| `Issue id` | A unique sequential number you assign to every row (1, 2, 3, …) |
| `Issue Type` | `Epic`, `Story`, or `Sub-task` |
| `Summary` | The title (for a story, the "As a … I want …" or a short title) |
| `Parent` | The `Issue id` of the parent row (story's parent = epic's id; sub-task's parent = story's id). Empty for epics. |
| `Epic Name` | **Epics only** — a short epic name (Jira requires this in addition to Summary) |
| `Description` | Full story text + Gherkin AC + Design Rationale |
| `Story Points` | Only if estimation is on; else omit the column |
| `Labels` | Optional — service name, `code-generated`, etc. |

**Hierarchy mechanics:**
- Give every row a unique `Issue id` (sequential integer).
- A Story's `Parent` = the Epic's `Issue id`. A Sub-task's `Parent` = the Story's `Issue id`.
- Epics need an `Epic Name` as well as a `Summary`.
- Put multi-line content (Description with Gherkin) in quoted CSV fields; escape embedded quotes
  by doubling them (`""`).

**Example:**

```csv
Issue id,Issue Type,Summary,Parent,Epic Name,Description,Labels
1,Epic,[BE] Notification Delivery,,Notification Delivery,"Deliver push/SMS notifications from domain events.",notification-service
2,Story,[BE] Push notification on tournament completion,1,,"As a participant, I want a push notification when I complete a tournament, so that I get immediate confirmation.

Given a participant finishes a tournament
When the completion event is published
Then a push notification is delivered within 5 seconds

Design Rationale (ADR kafka-migration.md): Kafka chosen for retry semantics.",notification-service
3,Sub-task,[BE] Add the completion-event consumer,2,,"Add a Kafka consumer for the tournament-completion event. It reads the event, builds the notification, and hands off to the sender. Wire it into the consumer registration.",notification-service
4,Sub-task,[BE] Apply Firebase batch chunking,2,,"Chunk notification batches to Firebase's 500-message limit before sending, so a large batch doesn't get rejected. Split, send each chunk, and aggregate the results.",notification-service
```

In the CSV, the `Summary` of a sub-task still starts with `[BE]`, and the `Description` carries the
short body (the same header + body content as the Markdown). Story and epic Summaries carry the
chosen title tag (`[BE]` by default, or none/custom per the up-front question).

Tell the developer to import via **Jira → System → External System Import → CSV** (not the
simple in-project CSV importer), and to map the `Parent` / `Epic Name` columns during the import
wizard.

### 7.3 Jira MCP Direct Push (future: not in this version)

> **Not implemented.** `/stories` exports Markdown and Jira CSV only. When asked to push, say so and
> give the CSV with the import steps (§7.2). This section is the design for when it's added.

When an Atlassian/Jira MCP connector is available, `/stories` can push the epics → stories → sub-tasks
directly into Jira instead of (or in addition to) exporting a CSV.

**How it works:**
1. **Detect the connector first.** Check whether an Atlassian/Jira MCP is connected. If it is **not**,
   don't offer this option — fall back to the Jira CSV (§7.2) and tell the developer the CSV is the
   path when no Jira MCP is connected. Never assume the connector exists.
2. **Confirm the target before pushing.** Ask the developer for the **project key** and confirm the
   issue-type scheme (Epic / Story / Sub-task names can differ per Jira project). Never push blind.
3. **Create parent-first** so child links resolve: **epics → stories → sub-tasks**, capturing each
   created issue's key and setting it as the `Parent` on its children. A sub-task created before its
   story exists will fail to link.
4. **Map the same fields as the CSV:** Summary, Issue Type, Parent, Epic Name (for epics), Description
   (the plain story body + Gherkin AC), and Points when Fibonacci points are enabled.
5. **Push in Plan Mode with approval** — present exactly what will be created (counts + hierarchy)
   and get a go-ahead before creating anything. Creating Jira issues is a real side effect; treat it
   like any write: confirm, then act. Report the created issue keys afterward.
6. **Auth note:** prefer API-token authentication (the stable path) if the connector asks; the older
   SSE/OAuth endpoint is being retired.

**Fallback:** if the push partially fails (e.g. rate limit mid-way), report which issues were created
(with keys) and which weren't, so the developer can resume or finish via CSV — never leave it
ambiguous.

CSV (§7.2) remains the default, connector-free path; MCP push is the direct option when the connector
is present.

---

## 8. Output File Handling

- Markdown → `stories.md` at the project root (or a path the developer specifies)
- Jira CSV → `stories-jira.csv` at the project root
- Present results inline first; write files on confirmation
- Never push to Jira or any external system in this version — export files only
- Never commit the generated files automatically (the developer commits, per the git rules)
