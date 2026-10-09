---
name: checkpoint
description: "Save progress mid-session — active files, decisions, test status, current task and next steps — then optionally /compact and keep working. For ending a session use /sleep."
allowed-tools: Read, Write, Bash, Grep, Glob
---

## Checkpoint — save progress, keep working

A marker for **"save what we did up to here"** while the session continues. It pairs naturally with
compaction: checkpoint the state, compact to free context, carry on with room.

**Closing the session instead?** Use **`/sleep`** — it flushes the journal, flags uncommitted work, and
writes what `/wake-up` reads.


### Capture
1. **Active files**: List all files modified in this session (`git diff --name-only`)
2. **Decisions made**: Summarize any architectural or design decisions
3. **Test status**: Run `dotnet test --no-build` and record pass/fail counts
4. **Current task**: What was being worked on and what's next
5. **Open questions**: Any unresolved issues or blockers

### Before saving — three things, in this order

1. **Flush the journal.** The distilled entry for the current stretch of work may not be written yet
   (the hook captures raw; Claude writes the useful version). Append a final entry to
   `.claude/journal.md` covering what was done, **why**, what was rejected, and what is open — the
   things that are expensive to reconstruct.
2. **Flag uncommitted work.** Run `git status --short`. If the tree is dirty, **say so prominently** —
   a laptop restart with uncommitted work in a worktree is how work is lost. Do not commit it (Claude
   never runs git writes); tell the developer what is dirty and let them decide.
3. **Note anything in flight** — a half-finished conversion, a test that was failing when you stopped,
   a question you were waiting on. "Where I stopped mid-thought" is worth more later than a tidy summary.

### Save
Write checkpoint to `~/.claude/sessions/checkpoint-<timestamp>.md`:

```markdown
# Session Checkpoint — <timestamp>

Repo: <absolute path of the repo root — `git rev-parse --show-toplevel`> · Branch: <current branch>
(one checkpoint per repo — if the session worked in several, write one per repo)

## Active Files
- src/OrderService.Domain/Entities/Order.cs (modified)
- tests/OrderService.Unit.Tests/Domain/OrderTests.cs (new)

## Decisions
- Chose cursor-based pagination over offset for order listing
- Using Money value object (record) instead of decimal for totals

## Test Status
- Unit: 47 passed, 0 failed
- Integration: 12 passed, 0 failed
- Architecture: 8 passed, 0 failed

## Current Task
Implementing OrderLineItem.AddQuantity with proper invariant checks

## Open tasks
(every task on the Ctrl+T list that is not completed — /wake-up restores these)
- [in_progress] Add AddQuantity invariant checks
- [pending] Integration test for order total recalculation
- [pending] Waiting: DBA sign-off on OrderLines index

## Next Steps
1. Write tests for quantity overflow edge case
2. Add integration test for order total recalculation
3. Run /full-review before PR

## Open Questions
- Should we cap line item quantity at 9999 or make it configurable?
```

### When to Checkpoint
- **Before closing the session** — end of day, a laptop restart, or a proven-dotnet upgrade.
- After completing a milestone, before starting the next.
- Before running `/compact` (compaction wipes conversation history — though `PostCompact` now
  re-injects the journal automatically).
- After a significant decision or design change.

### If you are upgrading proven-dotnet
Add one line to the checkpoint saying so, then:
1. `/checkpoint` (this command)
2. **Close Claude Code completely** — every window. The Roslyn MCP server holds files open, and an
   upgrade cannot replace a running binary.
3. Run `update.ps1`, answer `y` to the Roslyn rebuild.
4. Restart, then `/wake-up`.

### Coming back
Run **`/wake-up`**. It reads the newest checkpoint here, the work journal, standing decisions, and the
live git state — and where the checkpoint and the repo disagree, **the repo wins**.

### Then compact, if you want the room back
A checkpoint is the natural moment to compact: the state is saved, so compressing the conversation costs
nothing. After writing the checkpoint, say so plainly — *"state saved; run `/compact` if you want the
context back"* — and let the developer decide. Do not run it for them.

Auto-compaction is separate and already handled: `pre-compact` snapshots state and `PostCompact`
re-injects the journal afterwards, on both the manual and automatic triggers.

### Coming back later
`/wake-up` reads the newest checkpoint, the journal, standing decisions and live git state — and where
the checkpoint and the repo disagree, **the repo wins**.
