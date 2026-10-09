---
name: sleep
description: "End a session cleanly: flush the work journal, save state, and flag uncommitted work. Run it before closing, restarting the machine, or upgrading proven-dotnet — /wake-up reads what this writes."
allowed-tools: Read, Write, Bash, Grep, Glob
---

## Sleep — close the session

The write half of a pair: **`/sleep` before you stop, `/wake-up` when you return.**

Use it when you are **done for now** — end of day, a laptop restart, a proven-dotnet upgrade. For saving progress
*while you keep working*, use `/checkpoint` instead.

### What it does

```
/sleep
  │
  ├── 1. Flush the journal
  │      Append the final distilled entry to .claude/journal.md: what was done, WHY,
  │      what was tried and rejected, what is still open. The hook captures raw every
  │      turn, but the useful version is written by Claude — so the last stretch of
  │      work is exactly what goes missing when you stop.
  │
  ├── 2. Flag uncommitted work          (git status --short)
  │      Say plainly what is dirty, and in which worktree. A machine restart with an
  │      uncommitted tree is how work gets lost.
  │      DO NOT COMMIT — Claude never runs git writes. Report; the developer decides.
  │
  ├── 3. Note what is in flight
  │      A half-finished conversion, a test that was failing when you stopped, a
  │      question you were waiting on. Where you stopped mid-thought is worth more
  │      later than a tidy summary.
  │      Copy every task that is not completed (the Ctrl+T list) into the checkpoint
  │      under "Open tasks", with its status. The list belongs to this session; a new
  │      session starts empty, so the checkpoint is how it crosses over.
  │
  └── 4. Save the checkpoint file
         ~/.claude/sessions/checkpoint-<timestamp>.md — this is what /wake-up reads.
         Starts with `Repo: <repo root> · Branch: <branch>` so /handover can match it to its repo.
```

Then say, in one line, what `/wake-up` will find. If the session had a correction, a step the developer
taught, or something that broke, add one more line: "Worth a `/lessons` before closing." Nothing else —
this is the last thing before you close.

### Upgrading proven-dotnet
1. `/sleep`
2. **Close Claude Code completely** — every window. The Roslyn MCP server holds files open and a running
   binary cannot be replaced.
3. Run the install script again (`install.ps1` / `install.sh`), and `install-roslyn` if you use it.
4. Restart → `/wake-up`.

### Rules
- **Never commits.** It reports a dirty tree; the decision is the developer's.
- Writes the checkpoint where `/wake-up` looks for it.
- Journal first, so the last stretch of work is not the part that goes missing.
- Keep the closing summary to a line or two — you are leaving, not reading a report.
