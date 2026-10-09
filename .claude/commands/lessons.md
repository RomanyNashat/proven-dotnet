---
name: lessons
description: "Capture what this session taught (corrections, steps you taught, failures) as a report in ~/.claude/lessons/, outside any repo, then later fold each item into the harness (proven-dotnet or your team's layer) one approved change at a time. `/lessons` captures; `/lessons review` folds in."
allowed-tools: Read, Write, Edit, Bash, Grep, Glob
disable-model-invocation: true
---

## Lessons — make a mistake once

Uses the `lessons` skill. Read it first: it has the report format, the rules for what goes in it, and
the fold-in steps.

```
/lessons            → capture: write ~/.claude/lessons/YYYY-MM-DD-<slug>.md for this session
/lessons review     → fold in: walk the open items, one approved change at a time (run on the harness repo)
```

### `/lessons`: capture

1. Go through the whole session: every correction, every step the developer taught, everything that
   broke, and what worked.
2. Write the report in `~/.claude/lessons/` (create the folder if needed; on Windows it's
   `%USERPROFILE%\.claude\lessons\`). **Never inside a repo.**
3. Neutral labels for services and repos, no patient data, no secrets or internal URLs.
4. **Read-only for everything else.** Capture never edits the harness, the repo or memory.
5. Tell the developer the file path and how many items are in each section.

### `/lessons review`: fold in

Only on a harness repo: proven-dotnet or a team layer, which has `.claude/layer.json` (stop and say so
anywhere else).
1. Read the open items (`Status: open`) in `~/.claude/lessons/*.md`, or in the files attached to the chat.
2. Re-check each for names, patient data, internal URLs and secrets before anything enters the harness.
3. Present one item at a time, highest priority first: the lesson, its evidence, the exact diff to the
   harness file, and a recommendation. Wait for approve / edit / skip.
4. Apply approved changes on a branch with release notes (the repo's `CLAUDE.md` says how). Never
   commit to `main` directly.
5. Mark each item `applied (vX.Y.Z)` or `dropped (<reason>)` in its lessons file.

### When to run it
- At the end of any session with corrections, a new step, or something that broke. `/sleep` suggests it
  when it sees one.
- After `/handover`, every time: the handover is long and touches every repo, so it teaches the most.
