---
name: wake-up
description: "Load context at the start of a session: the work journal, the last checkpoint, open decisions and backlog, and the current git state. Read-only — it orients you without re-reading the whole repo. Run it when picking work back up after a break, a compaction, or a lost session."
disable-model-invocation: true
allowed-tools: Read, Bash, Grep, Glob, TaskCreate, TaskUpdate
---

## Wake up — pick the work back up

Reads what already exists and gives you a short brief. **Read-only.** It replaces the "let me re-read
the repo to work out where we were" opening that wastes a chunk of every session.

This is the **read** half of a pair: **`/sleep`** closes a session, **`/wake-up`** reopens it.
(`/checkpoint` is the mid-session equivalent — save progress without stopping.)

### What it reads (in order, skipping what isn't there)

1. **`.claude/journal.md`** — the distilled work journal for this repo. Read the **last 8 entries
   only** (from the eighth `## ` heading from the end); they say what was done, what was decided and
   why, what was rejected, and what is open. Older entries are history: open one only when a recent
   entry points to it.
2. **`.claude/.journal-raw.md`** — the hook's safety-net capture. Read this **only** when the journal
   is missing or clearly thin (the model didn't distil recent turns); it's unjudged and bounded, so
   treat it as a fallback, not the source.
3. **The newest checkpoint** — `~/.claude/sessions/checkpoint-*.md`, most recent by timestamp. Written
   by `/checkpoint`; it holds the current task, next steps, open questions and test status from when
   the session was wrapped up. Also check `~/.claude/sessions/<date>-state.json` for the last
   pre-compact snapshot. **Note its age** (now minus its timestamp); the age decides how it's used
   (see Rules).
4. **Decision records** — only the ADRs under `docs/decisions/` (or wherever the repo keeps them)
   changed in the last 14 days: `git log --since='14 days ago' --name-only -- docs/decisions`.
5. **Git state** — `git status --short` and `git log --oneline -5`. This is the ground truth about
   where the code actually is, and it corrects a stale journal.
   **Run from a folder that holds several repos** (not itself a repo): check only the repos the
   journal or the checkpoint names, plus any committed to since the checkpoint if that's cheap to tell.
   Never loop over every repo in one foreground call; a few dozen repos runs past the command timeout
   and the brief goes out half-checked. If the developer wants the full sweep, run it in the
   background and say the brief covers the named repos only.

### What it produces

A short brief — not a report:

```
Picking up: payment service, branch feat/PROJ-412-refunds

Last session (2 days ago)
  - Added the refund endpoint + validator; parity gate green.
  - Decided: refunds are idempotent via Idempotency-Key, not a dedupe table
    (avoids a second write path).
  - Rejected: soft-delete on refunds — the DBA wants an audit row instead.

Where the code is
  - 4 uncommitted files (RefundService.cs, RefundValidator.cs, +2 tests)
  - Last commit: "feat(PROJ-412): refund request validation"

Open
  - Audit-row schema still needs the DBA's sign-off.
  - /coverage not run since the validator was added.
```

If the checkpoint has an **Open tasks** section and is **3 days old or less**, put those tasks back
on the task list (Ctrl+T) with their status, so the list shows where the last session stopped. Drop
any that git shows are already done. This is the one write `/wake-up` makes, and it is session state,
not the repo.

**An older checkpoint is history, not state** (a weekend is fine; a vacation isn't). Put its age in
the brief's first line ("Last checkpoint: 24 days ago"), list its open tasks and "waiting on others"
items under "From <date> — still open?", and restore none of them until the developer says which are
still live. The same applies when a handover's return date is later than the checkpoint: the developer
was away, and the work has usually moved on without the journal.

Then stop. Don't start working — orienting and deciding are two different steps, and the developer
makes the second one.

### Rules
- **Read-only on the repo.** It never edits, applies, or commits. Restoring the task list is the only write.
- **Git state wins over the journal** when they disagree — the journal records intent, the repo records
  what actually happened. Say so plainly if they conflict.
- **Say what's missing.** No journal yet, no checkpoint, a repo that has moved on without entries — name
  it rather than inventing continuity. A confident-sounding brief built on nothing is worse than "there
  is no journal here yet."
- Keep it short. This is an orientation, not a status report — the developer wants to start working.
- Skip the raw capture unless the journal is absent or thin.
- **Read the ends, not the whole.** The journal's last 8 entries, the newest checkpoint, ADRs from
  the last 14 days, five commits. `/wake-up` runs at the start of most sessions, so what it reads is
  paid for again and again.
