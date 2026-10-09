---
name: stories
description: "Generate epics, user stories, and sub-tasks — from requirements, from existing code, or turn stories into code. A single direction-first wizard. Exports to Markdown and Jira CSV (with a documented path for direct Jira push later)."
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

## Story Engine

A single wizard that works in three directions. Run `/stories` and pick a direction first —
the command routes to the right agent from there.

### Step 0 — Direction (the first question)

```
/stories
  │
  └── "What do you want to do?"
       A. Generate stories FROM requirements   → business-analyst agent
       B. Extract stories FROM existing code    → code-analyst agent
       C. Turn existing stories INTO code       → hand off to /plan-feature
```

### Direction A — Requirements → Stories  (business-analyst)

```
├── Elicit: personas, goal/benefit, scope, constraints (one focused round)
├── Up-front questions: how many epics? · story split (per component / grouped)? · title tag ([BE]/none/custom)?
├── Generate: Epic → Story → Sub-task hierarchy, INVEST-checked
├── Stories: one per component (endpoint / job / consumer / cron), behavior-level
├── Acceptance criteria: Gherkin scenarios covering the real cases
├── Sub-tasks: always [BE], header + body, grouped by real work
├── Estimation? (Fibonacci — default no)
├── Review (story-reviewer): score the draft → auto-fix fails → report grade + fixes
├── Export? (Markdown / Jira CSV / both)
└── Offer hand-off to /plan-feature to start building
```

### Direction B — Code → Stories  (code-analyst)

```
├── Scope: source path → list folders → multi-select → "another path?" loop
│         (+ "include git history for context?" — default no)
├── Up-front questions: how many epics? · story split (per component / grouped)? · title tag ([BE]/none/custom)?
├── Read: code (primary, drives structure) + ADRs (rationale) [+ git if opted in]
├── Extract: service→Epic, component (endpoint/job/consumer/cron)→Story, class/method/repo/SP→Sub-task
├── Acceptance criteria: Gherkin scenarios covering the real cases the code enforces
├── Sub-tasks: always [BE], header + body, grouped by real work
├── Translate cryptic names (e.g. funnel) in the story, keep the real reference
├── Rationale: attach ADR "why" as Design Rationale notes on the matching stories
├── Flag (never encode) anything that looks like a bug
├── Estimation? (Fibonacci — default no)
├── Review (story-reviewer): score the draft → auto-fix fails → report grade + fixes
└── Export? (Markdown / Jira CSV / both)
```

### Direction C — Stories → Code

```
├── Take the approved stories + their Gherkin acceptance criteria
├── Hand each to /plan-feature (planner agent) → ordered, agent-assigned tasks
├── Implement via /tdd (test-first for the NEW code)
└── Review via /full-review, verify via /verify
```

### Key Design

- **Three questions up front.** Before generating, ask: how many epics, how to split into stories
  (one per component or grouped), and what title tag to use (`[BE]` default / none / custom). Honor
  all three.
- **Stories are per meaningful component, not strictly per endpoint.** A component is an endpoint,
  a background job, a consumer, a cron/console job, or a scheduled task. The story describes what
  that component does for the user/business — never at DTO/artifact level.
- **Translate jargon, keep the reference.** Cryptic names (e.g. a `funnel` route) get plain-word
  story text, but the real endpoint/component name stays as a reference.
- **Sub-tasks: always `[BE]`, header + body, grouped by real work.** Each sub-task is a title plus
  a short written description (not a bare bullet), carries the technical detail, and is grouped by
  the actual work — not one per code artifact, not a fixed template.
- **Write plain, precise, and human** at every level — epics, stories, sub-tasks. Like a clear
  ticket: conversational but exact. Avoid both failure modes — too stiff (AI tells: leverage,
  utilize, ensure, enable…) and too loose (figurative/hallway shorthand and borrowed UI metaphors:
  drill into, one-shot, fan out, paints, breadcrumb, badge…). Name the real thing; don't presume a
  UI. (See `skills/story-engine/` §1.5.)
- **Simplicity layer defaults to simple here.** Don't ask "simple or full?" (the one exception to the
  `simplicity` rule) — say it's simple, and switch to full if the developer asks.
- **A review gate runs before you see the map.** `/stories` runs three stages: generate → review →
  present. After generation, the `story-reviewer` agent scores the draft against INVEST and the story
  rules (voice/precision, real actor, granularity, sub-task hygiene, AC coverage, flags), auto-fixes
  what fails, and reports a pre-fix grade plus the fixes it made. This is a guard pointed at
  its own output — slop is caught here, not by you. (See `skills/story-review/`.)
- **Code drives structure, ADRs drive rationale** (Direction B). ADRs attach as notes; they don't
  define structure.
- **Two different test philosophies, kept separate.** Direction B reads existing behavior
  (characterization — same principle as `/coverage`). Direction C builds new code (TDD via `/tdd`).
- **The hierarchy is Epic → Story → Sub-task.** Technical steps are Sub-tasks, not Tasks.
- **Estimation is optional and off by default.** Fibonacci points only when you ask.

### Export Formats

| Format | File | Notes |
|--------|------|-------|
| Markdown | `stories.md` | Human-readable hierarchy tree with Gherkin AC and rationale |
| Jira CSV | `stories-jira.csv` | Imports via Jira's External System Import (handles Epic→Story→Sub-task). Includes Issue id / Issue Type / Parent / Epic Name columns. |
| Jira direct push | — | **Not in this version.** Documented as a future path in `skills/story-engine/` §7.3 (push epics/stories/sub-tasks straight to Jira via the Atlassian MCP connector). |

### Rules
- Export files only — no pushing to Jira or any external system in this version.
- Never auto-commit generated files (the developer commits, per the git rules).
- Read-only on the codebase in Direction B — produces stories, never modifies code.
- Never invent story points the developer didn't ask for.

### Skills & Agents
- Skill: `story-engine/` (formats, hierarchy, both extraction directions, exports)
- Skill: `story-review/` (the review-gate rubric, grading, auto-fix, report)
- Agents: `business-analyst` (Direction A), `code-analyst` (Direction B), `story-reviewer` (review gate)
- Reuses: `handover/` scoped directory-picker (Direction B scope), and the
  `/plan-feature` → `/tdd` → `/full-review` pipeline (Direction C)
