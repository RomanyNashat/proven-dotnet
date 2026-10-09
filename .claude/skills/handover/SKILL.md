---
name: handover
description: Vacation handover across repos: parallel per-repo analysis (journal, sessions, checkpoints, ADRs, git), source-labelled findings, 17-section template, Markdown/Teams/Notion output.
version: 2.0.0
---

# Handover Generation Skill

This skill contains all the knowledge needed to generate a vacation handover document
from git data, ADRs, non-code context, and interactive developer input. It is used
by `/handover` (main conversation), `handover-repo-analyst` (one per repo) and `handover-agent` (writer).

---

## 1. Git Analysis Procedures

### 1.1 Branch Discovery

```bash
# Detect default branch (master or main)
DEFAULT_BRANCH=$(git symbolic-ref refs/remotes/origin/HEAD 2>/dev/null | sed 's@^refs/remotes/origin/@@')
if [ -z "$DEFAULT_BRANCH" ]; then
  DEFAULT_BRANCH=$(git branch -l main master 2>/dev/null | head -1 | tr -d '* ')
fi

# All unmerged local branches
git branch --no-merged "$DEFAULT_BRANCH" --format='%(refname:short)|%(committerdate:short)|%(authorname)'

# All unmerged remote branches (catches branches not checked out locally)
git branch -r --no-merged "origin/$DEFAULT_BRANCH" --format='%(refname:short)|%(committerdate:short)|%(authorname)' | grep -v HEAD

# Recently merged branches (within lookback window — default 2 weeks)
# Portable: GNU (Linux), BSD (macOS), then git itself (works everywhere incl. Windows)
LOOKBACK_DATE=$(date -d '14 days ago' '+%Y-%m-%d' 2>/dev/null \
  || date -v-14d '+%Y-%m-%d' 2>/dev/null \
  || git log -1 --since='14 days ago' --format=%cd --date=short 2>/dev/null)
# Simpler still — prefer git's own relative dates and skip date(1) entirely:
#   git log --since='14 days ago' ...  (git parses this on every platform)
git branch --merged "$DEFAULT_BRANCH" --sort=-committerdate --format='%(refname:short)|%(committerdate:short)|%(authorname)' | head -20
```

### 1.1b The work journal — read this FIRST

Before mining git, read **`.claude/journal.md`** in the repo (and `.claude/.journal-raw.md` only if the
journal is thin). Git tells you *what changed*; the journal tells you **why**, what was tried and
rejected, and what is still open — which is most of what a handover actually needs and is exactly what
git cannot show.

Use it to pre-fill the sections that normally require interviewing the developer:
- **Decisions and rationale** → journal entries already record the why.
- **What was tried and abandoned** → the rejected options, so the next person doesn't redo them.
- **Known issues / open items** → the "Open" lines.
- **Gotchas** → errors hit and how they were resolved.

Then use the interactive enrichment step (Step 4) only for the **gaps the journal doesn't cover**,
rather than asking about everything. If there is no journal, say so and fall back to the full
interactive pass.

**Treat git as the correction layer:** where the journal and the repo disagree, the repo is what
happened. Note the conflict rather than silently trusting either.

### 1.2 Branch Analysis (per branch)

For each unmerged branch, run:

```bash
BRANCH="feature/some-branch"

# Commit log with structured output
git log "$DEFAULT_BRANCH".."$BRANCH" --oneline --format="%H|%an|%ae|%ad|%s" --date=short

# Diff stats (files changed, insertions, deletions)
git diff --stat "$DEFAULT_BRANCH".."$BRANCH"

# File names changed (for understanding scope)
git diff --name-only "$DEFAULT_BRANCH".."$BRANCH"

# Last commit date (for staleness detection)
git log -1 --format="%ad" --date=short "$BRANCH"

# All unique authors on this branch
git log "$DEFAULT_BRANCH".."$BRANCH" --format="%an <%ae>" | sort -u
```

### 1.3 Interpreting Git Data

**Branch name → ticket/purpose mapping:**
- `feature/<ticket>-<description>` → extract ticket ID and description
- `fix/<ticket>-<description>` → bug fix, extract ticket ID
- `hotfix/<description>` → urgent production fix
- `release/<version>` → release preparation
- Other patterns → infer from commit messages

**Staleness classification:**
- Last commit < 3 days ago → active
- Last commit 3–14 days ago → potentially paused (ask developer)
- Last commit > 14 days ago → stale (flag and ask developer)

**Multi-author detection:**
- If more than one unique author appears in the branch's commits, flag it
- Generate a colleague context-request message (see §5.3)

**Commit message → summary:**
- Group related commits by area (same files, same prefix)
- Synthesize into 1–3 sentence human-readable summary
- Use the conventional commit type to categorize: feat = new capability, fix = bug fix, refactor = restructuring, etc.

### 1.4 Recently Merged Branches

For merged branches within the lookback window:
```bash
# Get merge commit info
git log --merges --oneline --since="$LOOKBACK_DATE" "$DEFAULT_BRANCH"
```
These populate the "recently completed work" context in the handover. They show
the covering person what was recently shipped and might need monitoring.

---

## 1.7 Multi-repo handover — the default

Services are often separate git repos side by side under a parent folder. `/handover` runs from that
parent folder and works like this (full flow in `commands/handover.md`):

1. **Find the repos** — `scripts/discover_repos.py <parent> --since <date>` lists every repo where the
   developer committed since the date (matched by that repo's `user.email`/`user.name`), with commit
   count, last commit, branch, uncommitted files and never-pushed commits. The developer unticks.
2. **Analyse in parallel** — one `handover-repo-analyst` per repo, up to 6 per message. Each reads, in
   order: journal → surviving sessions (`scripts/extract_sessions.py`) → checkpoints → ADRs → git, and
   returns a fixed-shape, source-labelled summary. Every git command is `git -C "$REPO"`; never in the
   parent folder.
3. **Ask repo by repo** — never-pushed/uncommitted work first, then `[git only]` items, then confirm the
   rest in one go.
4. **Write** — `handover-agent` writes one document with a sub-section per repo, into the parent folder
   (never inside a repo).
5. **Journals** — with the developer's OK, append the analysts' reconstructed entries (§1.8).

A single repo is just the one-repo case of the same flow. §1.5 (services as folders inside ONE repo)
still applies to a monorepo.

### Source labels and precedence
Every item carries where it came from: `[journal]`, `[session YYYY-MM-DD]`, `[checkpoint YYYY-MM-DD]`,
`[ADR]`, `[git]`, `[context dump]`, or `[git only]` (no why known → becomes a question). Git wins on facts
about state; the journal wins over sessions on intent. Unconfirmed items keep their label in the final
document so the reader knows how solid each line is.

### Why sessions may be missing
Claude Code deletes session transcripts after `cleanupPeriodDays` (default 30; the proven-dotnet settings set 120). The
extractor reports the oldest surviving session per repo, and the summary's Coverage line shows it — so a
gap is visible, not silent.

## 1.5 Service Scoping Logic

When the developer chooses **scoped mode**, the handover focuses on specific services
rather than everything. This section defines how scoping works.

### 1.5.1 Service Discovery from Source Paths

The developer provides one or more source paths. For each path, list subdirectories:

```bash
# List all immediate subdirectories (each is a service/project)
ls -d <source_path>/*/ 2>/dev/null | xargs -n1 basename
```

Example: given path `C:\Projects\Shop\Source`, this might return:
```
OrderService
CatalogService
BasketApi
PaymentService
SharedLibraries
AuthService
ApiGateway
```

Present these as a multi-select list. The developer picks which services to include.

If the developer provides multiple source paths, accumulate all selections into a
single unified service list. Deduplicate by name (case-insensitive).

### 1.5.2 Branch-to-Service Matching

When filtering git branches to selected services, a branch matches if ANY of these are true:

1. **Branch name contains service name** (case-insensitive, with common variations):
   - `feature/SHOP-123-order-retry` matches "OrderService"
   - Match against: full name, kebab-case, shortened forms (e.g., "order" matches "OrderService")

2. **Changed files are under that service's directory**:
   ```bash
   git diff --name-only "$DEFAULT_BRANCH".."$BRANCH" | grep -i "<service-name-or-directory>"
   ```

3. **Commit messages reference the service**:
   ```bash
   git log "$DEFAULT_BRANCH".."$BRANCH" --oneline | grep -i "<service-name>"
   ```

If a branch matches multiple selected services, include it once and note all related services.

If a branch matches NO selected services, exclude it from the scoped handover.

### 1.5.3 ADR Scoping

In scoped mode, only scan ADR directories matching selected services:

```bash
# Instead of scanning all ADR directories:
#   find docs/decisions -name "*.md" -type f
# Only scan selected service directories:
for SERVICE in "${SELECTED_SERVICES[@]}"; do
  find docs/decisions -name "*.md" -type f 2>/dev/null | grep -i "$SERVICE"
done
```

### 1.5.4 Section Scoping Rules

Not all 17 sections are scoped. Some are inherently about the developer (not services).

| Section | Scoped? | Behavior in Scoped Mode |
|---------|---------|------------------------|
| 1. General Info | No | Always full — it's about you, not services |
| 2. Distribution Matrix | **Yes** | Only list selected services |
| 3. Active Tickets | **Yes** | Only branches matching selected services |
| 4. Services Owned | **Yes** | Only selected services |
| 5. Scheduled Jobs | **Yes** | Only jobs related to selected services |
| 6. Monitoring | **Yes** | Only dashboards for selected services |
| 7. On-Call | **Yes** | Scoped to selected services' on-call |
| 8. Environment | **Yes** | Only deployments/flags for selected services |
| 9. Pending PRs | **Yes** | Only PRs for branches matching selected services |
| 10. Communications | No | Always full — emails are personal, not per-service |
| 11. Meetings | No | Always full — meetings are personal |
| 12. Deadlines | No | Always full — deadlines may cross services |
| 13. Escalation | **Yes** | Only escalation paths for selected services |
| 14. Access | No | Always full — credentials are shared across services |
| 15. Known Risks | **Yes** | Only risks related to selected services + their ADRs |
| 16. OOO Plan | No | Always full — it's about you |
| 17. Return Plan | No | Always full — it's about you |

### 1.5.5 Full Auto Mode

In full auto mode, no scoping is applied. All branches, all ADRs, all services,
all sections are included without filtering. This is the default behavior described
in all other sections of this skill.


## 1.8 Reconstructed journal entries

For work in the period a repo's journal doesn't cover, the analyst proposes entries in the normal journal
format, marked with their origin:

```markdown
## Reconstructed history — 2026-09-30 (from git/sessions/checkpoints)

## 2026-07-14 — Refund validation (reconstructed from git + session 2026-07-14)
Did: added the refund validator and idempotency key handling on feat/PAY-12-refunds.
Why: duplicate refund requests from the mobile client (developer confirmed during handover).
Open: PR still waiting for QA.
```

They are written only after the developer approves the list, only to `<repo>/.claude/journal.md`, and
only once that file is git-ignored (`git -C <repo> check-ignore`; if not ignored, add it to
`.git/info/exclude` — local, no tracked change). Anything the developer corrected during the questions is
written as corrected. No patient data, secrets or credentials, same as every journal entry.

---

## 1.6 Context Digestion

When the developer provides a free-form context dump (Step 0.5), Claude must parse
unstructured natural language into structured handover data. This section defines
the extraction and routing rules.

### 1.6.1 What to Extract

Scan the developer's input for these data types:

| Data Type | Signals to Look For | Example |
|-----------|-------------------|---------|
| **Tickets** | `#123`, `PROJ-456`, "ticket", "issue", "story", "bug" | "PROJ-456 is the order batching work" |
| **Service references** | Service names, project names, repo names | "the catalog service has a Redis memory issue" |
| **Status indicators** | "on hold", "blocked", "waiting for", "paused", "done", "in progress", "deployed", "merged" | "ticket #123 is on hold because..." |
| **Blockers** | "blocked by", "waiting on", "depends on", "can't proceed until" | "blocked by the gateway team" |
| **Cross-team dependencies** | Team names, external contacts, "they need to", "we're waiting for them" | "mobile team expects the API changes by next sprint" |
| **Environment state** | "deployed to staging", "not in prod yet", "feature flag", "config change" | "patched in staging but not prod" |
| **Risks and concerns** | "worried about", "might break", "keep an eye on", "could be a problem" | "memory leak we found last week" |
| **Timelines** | Dates, "by next week", "end of sprint", "Thursday" | "load test window on Thursday" |
| **People** | Names, roles, "talk to", "ask", "contact" | "check with Ahmed on the gateway status" |

### 1.6.2 Section Routing

Once extracted, route each piece of data to the appropriate handover section(s).
A single piece of context can map to multiple sections.

| Extracted Data | Primary Section | Secondary Section(s) |
|---------------|----------------|---------------------|
| Ticket with status | 3 (Active Tickets) | — |
| Ticket that is blocked | 3 (Active Tickets, status: Blocked) | 15 (Known Risks) |
| Service current state | 4 (Services Owned, known issues) | 8 (Environment) |
| Deployment not yet in prod | 8 (Environment) | 15 (Known Risks) |
| Cross-team dependency | 10 (Communications) | 12 (Deadlines) if timeline given |
| Pending conversation | 10 (Communications) | — |
| Risk or concern | 15 (Known Risks) | — |
| Upcoming deadline | 12 (Deadlines) | — |
| Person to contact about X | 13 (Escalation) | — |
| Monitoring concern | 6 (Monitoring) | 15 (Known Risks) |
| Job/process issue | 5 (Scheduled Jobs) | 15 (Known Risks) |

### 1.6.3 Merging with Other Sources

Context dump data is **additive** — it supplements git, ADR, and non-code-context data,
it doesn't replace them. During document generation (Step 5):

1. If the context dump mentions a ticket that also appears as a git branch, **merge** them:
   use the git data for commit details and the context dump for reasoning/blockers.
2. If the context dump mentions something already in `non-code-context.md`, the context
   dump takes precedence (it's more recent — the developer just said it).
3. If the context dump introduces entirely new information (no git branch, no ADR, no
   non-code-context entry), create new entries in the appropriate sections.

### 1.6.4 De-duplication in Step 4

During Interactive Enrichment (Step 4), skip questions the developer already answered
in the context dump. For example:
- If they said "feature/kafka-migration is blocked on infrastructure team" → don't ask
  about that branch's status or reasoning again. Confirm what you have and move on.
- If they mentioned a risk → don't ask "any known risks?" without acknowledging it first.

---

## 2. ADR Collection

### 2.1 Discovery

```bash
# Find all ADR files (docs/decisions by default; also the common alternatives)
find docs/decisions docs/adr -name "*.md" -type f 2>/dev/null
find . -path "*/Decisions/ADR/*.md" -type f 2>/dev/null | grep -v node_modules
```

### 2.2 Parsing

For each ADR file, extract these fields:
- **Title**: first `# ` heading
- **Date**: line matching `**Date:**`
- **Service**: line matching `**Service:**`
- **Status**: line matching `**Status:**` — values: `in-progress` or `completed`
- **Decision**: content under `## Decision`
- **Alternatives**: content under `## Alternatives Considered`
- **Reasoning**: content under `## Reasoning`

### 2.3 Classification

| ADR Status | Still Exists? | Meaning | Handover Action |
|-----------|---------------|---------|-----------------|
| in-progress | Yes | Active decision, work ongoing | → Section 3 (Active Tickets) + Section 15 (Known Risks) |
| completed | Yes | Forgotten cleanup — work is done | → Flag for developer to delete; optionally note as context |
| (any) | Deleted | Work completed and merged | No action needed |

### 2.4 Mapping ADRs to Sections

- **In-progress ADRs** are the most valuable for handover. They represent decisions
  the covering person may need to continue or be aware of.
- Map each in-progress ADR to its related branch (by service name match or developer confirmation)
- If an ADR has no corresponding branch, it represents investigation/decision work
  that hasn't reached code yet — include in Section 15 (Known Risks / Watch Items)

---

## 3. Non-Code Context File

### 3.1 Location and Reading

```bash
# The developer's own file (the installer never writes here)
cat ~/.claude/handover/non-code-context.md 2>/dev/null
```

If it isn't there, say so, ask about the non-code sections interactively, and offer to create it from
the template the layer installs at `~/.claude/proven/non-code-context.template.md`, so next time it's
already filled.

### 3.2 Section Detection

Parse the file for these section headings (case-insensitive):
- `## On-Call` → maps to Section 7
- `## Recurring Meetings` → maps to Section 11
- `## Monitoring` → maps to Section 6
- `## Pending Communications` → maps to Section 10
- `## Upcoming Deadlines` → maps to Section 12
- `## Scheduled Jobs` → maps to Section 5
- `## Environment Notes` → maps to Section 8
- `## Services Owned` → maps to Section 4
- `## Escalation Contacts` → maps to Section 13
- `## Access & Credentials` → maps to Section 14
- `## Handover Distribution` → maps to Section 2
- `## Return Plan` → maps to Section 17
- `## Out-of-Office Plan` → maps to Section 16

### 3.3 Content vs Placeholder Detection

A section is considered **empty** if:
- It contains only `[bracketed placeholders]`
- It contains only the template text with no custom content
- It has no content below the heading

A section is considered **filled** if:
- It contains actual names, dates, links, or descriptions
- The bracketed placeholders have been replaced with real data

Empty sections become interactive questions in the enrichment step.

---

## 4. The 17-section template

The full template — every section, what goes in it, and the ordering — lives in
**`reference/template.md`** beside this skill. Read it when you are ready to generate the
document. It is kept out of this file so the skill body stays small enough to load cheaply on
every invocation.

## 5. Interactive Enrichment

### 5.1 Branch Enrichment Questions

For each unmerged branch, present in this order:

```
Branch: feature/SHOP-1234-kafka-migration
Commits: 12 commits, last on 2026-03-28
Authors: Sam Lee
Changes: 8 files (+420, -85) — mostly in src/OrderService/

My summary: This branch adds Kafka event-driven order confirmation,
replacing the synchronous HTTP approach. The work includes a new consumer
service, dead letter queue configuration, and updates to the order
publisher.

Questions:
1. What's the reasoning or context a covering person should know?
   (empty to skip)
2. Status: active / paused / blocked / abandoned?
3. Does this branch have an open PR (MR on GitLab)? If so, what's its status?
```

### 5.2 Non-Code Section Questions

For each empty section, ask in this format:

```
Section 7 — On-Call / Incident Response

This section covers: your on-call rotation status, who's covering your shift,
the escalation runbook, any recent incidents, and the incident response cheat sheet.

What should I include? (empty to skip, I'll mark it as "Ask [your name]" in the handover)
```

### 5.3 Colleague Context-Request Message Template

When a branch has commits from multiple authors, generate:

```
Hey [colleague name],

I'm preparing my vacation handover (out [start] → [end]). You have commits
on branch `[branch name]`:

[list of their commit messages, last 5]

Can you give me a quick summary of:
1. What you were working on in this branch
2. Anything the covering person should know
3. Current status from your side

Thanks!
```

### 5.4 General Gap Questions

Ask these at the end of the enrichment step:

1. "Any work that happened outside git? Investigations, POCs, conversations with
   other teams that the covering person should know about?"
2. "Any known risks or things to watch that we haven't captured above?"
3. "Who should receive this handover? (names or team channel)"
4. "Anything else you want to add to any section?"

---

## 6. Format generation rules

Markdown, Teams and Notion output rules live in **`reference/formats.md`** beside this skill.
Read it at output time, once the content exists.

## 7. Data Source → Section Master Mapping

This is the definitive mapping table. The handover agent uses this to know where to
pull data from for each section. A team `handover-<team>` skill, if installed, adds pre-fills on top
(see `reference/template.md`).

| # | Section | Git | ADRs | Non-Code File | Interactive | Scoped? |
|---|---------|-----|------|--------------|-------------|---------|
| 1 | General Info | — | — | OOO Plan | Vacation dates (Step 0) | No |
| 2 | Distribution Matrix | — | — | Handover Distribution | Backup assignments | **Yes** |
| 3 | Active Tickets | Unmerged branches | In-progress (investigation tasks) | — | Reasoning, status, blockers | **Yes** |
| 4 | Services Owned | — | — | Services Owned | Links, known issues | **Yes** |
| 5 | Scheduled Jobs | — | — | Scheduled Jobs | Job details | **Yes** |
| 6 | Monitoring | — | — | Monitoring | Dashboard links, thresholds | **Yes** |
| 7 | On-Call | — | — | On-Call | Coverage, incidents | **Yes** |
| 8 | Environment | Recent merges → deployments | — | Environment Notes | Flags, configs | **Yes** |
| 9 | Pending PRs | Unmerged branches → PRs | — | — | PR status, reviewer handover | **Yes** |
| 10 | Communications | — | — | Pending Communications | Email details | No |
| 11 | Meetings | — | — | Recurring Meetings | Coverage assignments | No |
| 12 | Deadlines | — | — | Upcoming Deadlines | Dates, owners | No |
| 13 | Escalation | — | — | Escalation Contacts | Contact details | **Yes** |
| 14 | Access | — | — | Access & Credentials | System access refs | No |
| 15 | Known Risks | Stale branches | In-progress (decisions in flux) | Monitoring (approaching thresholds) | Additional risks | **Yes** |
| 16 | OOO Plan | — | — | Out-of-Office Plan | Checklist confirmation | No |
| 17 | Return Plan | — | — | Return Plan | First-day plan | No |

---

## 8. Output File Handling

### 8.1 File Locations

All output files are written to the current project root directory by default.
If the developer specifies a different location during Step 0, use that instead.

```
project-root/
├── vacation-handover.md                  # Markdown (if selected)
├── vacation-handover-teams-medium.txt    # Teams medium (if Teams selected)
├── vacation-handover-teams-short.txt     # Teams ultra-short (if Teams selected)
└── vacation-handover-notion.md           # Notion (if selected)
```

### 8.2 Post-Generation Actions

After writing all files, offer:
1. "Want me to update your `non-code-context.md` with the information you provided during the wizard?"
2. "Want me to list the ADRs that should be cleaned up (completed but not deleted)?"
3. "Anything you want to change in the generated documents before sharing?"

### 8.3 Reminder

After generation, remind the developer:
- Review the document before sharing
- Schedule a 30-min sync with backup(s) to walk through it
- Share the full Markdown doc as the source of truth; Teams messages are summaries
- Set up OOO auto-reply, calendar blocks, and Slack/Teams status
