---
name: handover
description: "Generate a vacation handover across all the repos you worked in. Finds them, analyses each in parallel (journal, surviving Claude sessions, checkpoints, ADRs, git), asks you only what it can't infer, and writes Markdown/Teams/Notion files. Run from the folder that holds your repos, before any extended absence."
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

## Vacation Handover

**This command runs in the main conversation** — it has to talk to you. The heavy reading is done by
`handover-repo-analyst` agents, **one per repo, in parallel**; the writing by `handover-agent`.
Use the task list (Ctrl+T) for the steps below so progress is visible.

**Run it from the folder that holds your repos** (e.g. `...\source\work`). The output is written there,
never inside a repo, so the handover can't be committed by accident.

```
/handover
  │
  ├── 0. Setup  (you)
  │    ├── Since when?  default: the date of the last vacation-handover.md in this folder;
  │    │                if none, ask — suggest ~4 months back
  │    ├── Find your repos:
  │    │     python "$HOME/.claude/skills/handover/scripts/discover_repos.py" "<this folder>" --since <date>
  │    │     → table: repo · your commits · last commit · branch · uncommitted · unpushed
  │    │     → you untick any you don't want. "Another folder?" → repeat, then continue
  │    ├── team pre-fills (a handover-<team> skill, if installed) · output formats · vacation dates
  │    └── Context dump (optional): "tell me everything about the state of your work"
  │
  ├── 1. Analyse — one handover-repo-analyst per repo, up to 6 IN ONE MESSAGE (parallel), then the next 6
  │    each gets: REPO, SINCE, SKILL_DIR="$HOME/.claude/skills/handover", the context-dump lines for that repo
  │    each reads, in order: journal → surviving sessions → checkpoints → ADRs → git
  │    each returns a fixed-shape summary, every item labelled by source
  │
  ├── 2. Non-code context — read ~/.claude/handover/non-code-context.md; note empty sections
  │
  ├── 3. Your questions — repo by repo  (you)
  │    for each repo, in the order picked:
  │      a. "⚠ Before you leave": uncommitted / never-pushed work → push it yourself, or note why not
  │      b. items marked [git only] or conflicting → the analyst's "Ask:" questions, one at a time
  │      c. everything else shown already filled in → confirm or correct in one go
  │      d. multi-author branches → a copy-paste message for the colleague
  │    then: empty non-code sections one at a time, then general gaps
  │    (work outside git? risks not captured? who receives this?)
  │
  ├── 3.5 Plan Mode only: show all 17 sections as a PREVIEW; write nothing until you say go
  │
  ├── 4. Write — handover-agent gets the merged, confirmed data → files in this folder
  │
  └── 5. After  (you)
       ├── Journals: "N reconstructed entries for M repos — save them?"  (list titles per repo)
       │     on yes, per repo: make sure .claude/journal.md is git-ignored
       │     (git -C <repo> check-ignore; if not ignored, add it to .git/info/exclude — local only,
       │     no tracked file changes), then append the entries under
       │     "## Reconstructed history — <today> (from git/sessions/checkpoints)"
       ├── Offer to update non-code-context.md with what you told it
       └── Final reminder: the list of repos with work still not pushed
```

### Why these sources, in this order
| Source | Gives | Limit |
|--------|-------|-------|
| **Journal** (`.claude/journal.md` per repo) | The why, decisions, what's open | Only exists since the journal was introduced |
| **Sessions** (`~/.claude/projects`) | What you and Claude discussed and decided | Claude Code deletes them after `cleanupPeriodDays` (proven-dotnet sets 120) |
| **Checkpoints** (`~/.claude/sessions`) | Where you stopped, open tasks | Only when you ran `/checkpoint` or `/sleep` |
| **ADRs** | In-flight decisions | Only where written |
| **Git** | What changed, branches, merges, ticket keys, unpushed work | Never the why |
| **You** | Everything else | Asked only about what the others can't answer |

### Rules
- **Never pushes, commits, checks out or stashes.** It lists what isn't pushed; you push.
- **Never writes inside a repo**, except the reconstructed journal entries you approve (git-ignored).
- **No patient data or secrets** in the handover or journals — sessions are summarised, never quoted.
- **Show what was inferred before asking.** You confirm or correct; you never start from blank.
- **Skip = skip.** No forced completeness; an empty section says it's empty.
- Colleague messages are generated for you to send — never sent automatically.

### The 17 sections
1 General Info · 2 Distribution Matrix · 3 Active Tickets (a sub-section per repo) · 4 Services Owned ·
5 Scheduled Jobs · 6 Monitoring · 7 On-Call · 8 Environment & Deployment · 9 Pending PRs ·
10 Pending Communications · 11 Meetings · 12 Deadlines & Releases · 13 Escalation · 14 Access ·
15 Known Risks (unpushed work first) · 16 OOO Plan · 17 Return Plan

Template and format rules: `skills/handover/reference/template.md`, `reference/formats.md`.

### Output
`vacation-handover.md` (+ `-teams-medium.txt`, `-teams-short.txt`, `-notion.md` if chosen) in the
folder you ran it from. Review before sharing, and book a 30-minute walkthrough with your backup.

Then run `/lessons` in this same session: a handover teaches more about how Claude should work than
any other run, and the session that made the mistakes is the one that remembers them.
