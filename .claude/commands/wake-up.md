---
name: wake-up
description: "Load context at the start of a session: the work journal, the last checkpoint, open decisions and backlog, and the current git state. Read-only — it orients you without re-reading the whole repo. Run it when picking work back up after a break, a compaction, or a lost session."
allowed-tools: Read, Bash, Grep, Glob, TaskCreate, TaskUpdate
---

## Wake up — pick the work back up

Reads what already exists and gives you a short brief. **Read-only.** It replaces the "let me re-read
the repo to work out where we were" opening that wastes a chunk of every session.

This is the **read** half of a pair: **`/sleep`** closes a session, **`/wake-up`** reopens it.
(`/checkpoint` is the mid-session equivalent — save progress without stopping.)

### What it reads (in order, skipping what isn't there)

1. **`.claude/journal.md`** — the distilled work journal for this repo. The tail is what matters: the
   last few entries say what was done, what was decided and why, what was rejected, and what is open.
2. **`.claude/.journal-raw.md`** — the hook's safety-net capture. Read this **only** when the journal
   is missing or clearly thin (the model didn't distil recent turns); it's unjudged and bounded, so
   treat it as a fallback, not the source.
3. **The newest checkpoint** — `~/.claude/sessions/checkpoint-*.md`, most recent by timestamp. Written
   by `/checkpoint`; it holds the current task, next steps, open questions and test status from when
   the session was wrapped up. Also check `~/.claude/sessions/<date>-state.json` for the last
   pre-compact snapshot.
4. **Decision records** — any ADRs under `docs/decisions/` (or wherever the repo keeps them) that
   changed recently.
5. **Git state** — current branch, uncommitted changes, and the last few commit subjects. This is the
   ground truth about where the code actually is, and it corrects a stale journal.

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

If the checkpoint has an **Open tasks** section, put those tasks back on the task list (Ctrl+T) with
their status, so the list shows where the last session stopped. Drop any that git shows are already
done. This is the one write `/wake-up` makes, and it is session state, not the repo.

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
