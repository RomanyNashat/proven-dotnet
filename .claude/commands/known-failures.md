---
name: known-failures
description: "The tests already red in this repo before any change: list them, recheck them, drop the ones that pass, and plan fixing the rest."
allowed-tools: Read, Bash, Grep, Glob
disable-model-invocation: true
---

# /known-failures

The repo's `.claude/known-failing-tests.txt` lists the tests that were red before any change: the TDD
gate writes it from the session's first full `dotnet test` run, made before the first edit on a tree with
nothing uncommitted. Those tests don't hold a turn. The file is local (its path is in `.git/info/exclude`),
and each name carries the day it was first seen red.

`/known-failures` with no argument lists them; `/known-failures recheck` runs only those tests and drops
the ones that pass now. `/known-failures refresh` is the full run again.

## Steps

1. **List.** `python ~/.claude/hooks/proven/known-failures.py list`. No file: say so, and offer the full run
   (step 3).
2. **Recheck** (`recheck`). `python ~/.claude/hooks/proven/known-failures.py recheck`, adding
   `--target <solution or project>` when the folder holds more than one. It runs
   `dotnet test --filter "FullyQualifiedName~<name>|..."` and rewrites the list; a run that doesn't build,
   or that runs no test, changes nothing. The filter is vstest's: on Microsoft.Testing.Platform runners,
   say the recheck can't filter there and use `refresh`.
3. **Refresh** (`refresh`). Only with nothing edited yet in this session and nothing uncommitted: run
   `dotnet test`, unfiltered, and read what the gate added to the context. Otherwise say why it can't run.
4. **Report.** One table: test, red since, and what it tests in a few words (read the test). Group the ones
   with one cause (the same exception, the same missing container or setting). Then the first one to fix and
   why: the oldest, or the one blocking the most.
5. **Plan the fixes, ask first.** Offer to write each still-red test up as an issue in the team's tracker,
   one per cause; create nothing until the developer says yes. A fix goes through `tdd-guide` like any
   bug: the red test is already there.

Never delete a name by hand to quiet the gate: a test leaves the list by passing.
