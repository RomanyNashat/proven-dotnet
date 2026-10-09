---
name: remediation-parity
description: Safety gate for tools that edit code. Starts from a committed tree; tiered — refactors get full behavior-parity (characterization test first if none), features run-tests, trivial none; optional worktree isolation for large conversions.
---

# Remediation Parity — prove an edit is safe before presenting it

Every tool that changes code makes an implicit promise. A refactor promises "same behavior,
cleaner code." A feature promises "new behavior, nothing else broken." This skill is how the tool
*keeps* that promise instead of just asserting it. It runs after the edit is generated, before the
edit is presented as done.

The rule it exists to enforce: **never tell the developer a change is safe unless it was verified —
and when it can't be verified, say so plainly.**

## The three tiers — match the gate to the edit

The gate's strength depends on what kind of edit was made. Don't run a heavy parity check on a
two-line feature tweak; don't skip it on an AutoMapper removal.

**Tier 1 — Refactor (behavior must be preserved) → FULL PARITY GATE.**
Any tool whose promise is "same behavior": `refactor-cleaner`, a library swap (AutoMapper to explicit
mapping), a rename pass. The question is "does it still do exactly what it did?" — so we prove behavior is
identical (see the parity procedure below).

**Tier 2 — Feature (behavior is meant to change) → RUN-THE-TESTS GATE.**
Tools that intentionally change behavior: `/plan-feature`, `/tdd`, `/scaffold`. A "did behavior
change?" check is meaningless here — it's *supposed* to. Instead: run the existing test suite to
confirm unrelated things didn't break, and (for TDD) confirm the new tests pass. Report failures
plainly.

**Tier 3 — Trivial → NO GATE.**
A comment, a rename of a single local variable, a formatting change. A gate here is pure friction.
Skip it.

If unsure which tier, ask: *is the promise "same behavior" or "new behavior"?* Same → Tier 1.
New → Tier 2. Neither meaningfully → Tier 3.

## Before the first edit: a committed tree (Tier 1 and Tier 2)

Edits land on a tree the developer has committed, so the change is exactly `git diff`, nobody's work
in progress is mixed into it, and undoing it is one command the developer runs.

1. `git status --porcelain` (read-only). **Clean** → note the starting commit (`git rev-parse HEAD`)
   and go on.
2. **Dirty** → stop and say so, listing the changed files: "Commit or stash these first, so my change
   is the only thing in the diff." Claude never commits or stashes for the developer. If the developer
   says to continue anyway, continue, and say in the report that the diff mixes their changes with
   Claude's.
3. At the end, the change set is `git diff <start>`; that's what goes in front of the developer with
   the parity outcome. To undo: `git restore .` (and `git clean` for new files), run by the developer.

## Isolation in a worktree (optional, experimental)

For a **large** Tier-1 conversion (a library swap across a whole service, a refactor over a big area), the tool offers to run the agent in its own git worktree, so the developer's tree isn't touched
at all until the result is approved. Offer it; don't default to it. It costs a fresh restore and full
build in the worktree, which on a locked-down corporate laptop can take minutes.

How it runs:
- The orchestrating command launches the agent with `isolation: "worktree"`. The installed settings set
  `worktree.baseRef: "head"`, so the worktree starts from the developer's current commit (it never
  includes uncommitted changes: one more reason for the committed-tree step above).
- **Roslyn is read-only inside it.** The Roslyn server has the main checkout loaded, so a
  `rename_symbol` with `preview: false` would edit the developer's files, not the worktree's. A hook
  (`worktree-guard.py`) blocks that call from inside `.claude/worktrees/`; renames are
  done as file edits, or previewed and applied after approval.
- The parity procedure below runs inside the worktree.
- **Coming back:** on approval, Claude applies the worktree's diff to the developer's tree as ordinary
  file edits (no git command that writes), then builds and runs the tests there once more.
- **Cleaning up** is the developer's: `git worktree remove .claude/worktrees/<name>` and
  `git branch -D worktree-<name>`. Claude prints both. A worktree with changes is never removed
  automatically.
- Once per repo, the developer adds `.claude/worktrees/` to `.git/info/exclude` (local, not shared), so
  worktrees don't show up as untracked files.
- Gitignored files the build needs (an `appsettings.Development.json`, for example) aren't in a fresh
  checkout. A `.worktreeinclude` file (gitignore syntax) at the repo root copies them in.

## The full parity procedure (Tier 1)

The hard case — and the common one — is a refactor of code that **has no tests** (e.g. an
AutoMapper mapping nobody ever wrote a test for). The gate does not rely on existing tests; it
**pins the current behavior itself, before the change.**

1. **Locate what's affected.** Find the thing being changed and everything that calls it. When the
   `proven-roslyn` MCP is connected, use `find_callers` / `find_references` for this — it's exact — and
   `find_tests_for_symbol` to see what tests already cover it (if it comes back uncovered, that's the
   signal to characterize it first).
   Fall back to reading files if it's not connected.
2. **Pin current behavior (characterization).** Before editing, capture what the current code
   produces for representative inputs — snapshot the outputs into a characterization test. Reuse the
   `test-coverage` skill / `coverage-analyst` machinery, which already writes characterization tests.
   For a mapping: build sample source objects, run the *current* mapping, record the exact output.
3. **Apply the change**: on the committed tree (the starting commit is the way back), or inside the
   worktree when running isolated. Nothing is committed by Claude either way.
4. **Run the characterization test against the new code.** Same inputs → compare outputs.
5. **Report one of the three outcomes below.**

## Three outcomes — and the third is the point

The gate never returns a binary "done / not done." It returns one of:

**(a) Proven identical** ✓ — the characterization test passes against the new code; behavior is
preserved. Present the change as safe, and say what was verified ("42 mappings, outputs identical
on N sample inputs").

**(b) Proven different** ✗ — outputs differ. Do NOT present it as done. Report *exactly what moved*:
"the new mapping trims trailing whitespace on `Name`; the old one didn't." Let the developer decide
whether that's an acceptable change or a regression.

**(c) Can't prove it** ⚠ — the honest third bucket, and the whole reason this skill is trustworthy.
Some code can't be pinned:
   - **Non-deterministic** — the code reads `DateTime.Now`, a random, a DB, an external call, so the
     same input doesn't give the same output.
   - **Inputs too complex** — the object graph can't be reliably constructed to characterize.
   In this case, say so plainly: "Couldn't verify behavior is preserved here — this mapping calls
   the database, so I can't pin it deterministically. Needs human review." Then hand it to the
   developer. **Never dress up (c) as (a).** A gate that admits what it couldn't check is worth more
   than one that always says "done."

## What this skill is NOT

- Not a replacement for the developer's judgment — it surfaces evidence, the developer decides.
- Not a test-writer for coverage's sake — the characterization tests it generates exist to pin
  behavior for the parity check; whether they're kept is the developer's call.
- Not a gate that blocks — it *reports*. It never silently rewrites or refuses; it shows what it
  found and lets the plan-first / approval flow proceed.

## How tools call it

Every Tier-1 editing tool calls this skill as its final step before
presenting a change:

> committed-tree check → generate the edit → **remediation-parity** (pin → apply → compare → classify) →
> present with the outcome (a/b/c) attached, in Plan Mode, for approval.

The tool never presents a Tier-1 change without a parity outcome attached.
