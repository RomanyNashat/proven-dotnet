# Eval results: the fix eval, round five

## 2026-10-29: a task that doesn't hand over the setting, three runs per arm (proven-dotnet 0.3.4)

Held out: `LabResults` was written after the skills, and nothing in the harness changed for it. Each run
fixed its own copy outside the repo, with `task.md` word for word. Builds were allowed; PostgreSQL and
Docker weren't there (the local server was stopped for the whole run). The copies are in `runs/` as the
runs left them, without their own test projects, with their reports and reviews, and `fix-evals.yml`
grades them.

| | With proven | Rules only | No rules |
|---|---|---|---|
| **Findings fixed** (3 runs × 8) | **24 / 24** | **20 / 24** | **10 / 24** |
| Regressions kept (3 runs × 6) | 18 / 18 | 18 / 18 | 12 / 18 |
| Builds | 3 / 3 | 3 / 3 | 3 / 3 |
| Missed | none | K05 in all three, K03 in one | K05 and K08 in all three, K03 and K07 in one; no-rules-1 failed every test |
| Test methods it added | 51, 61, 49 | 36, 28, 33 | 0, 0, 0 |
| Work per run (Claude Code's own estimate) | 3 reviewers about $1.90, a consolidation about $0.70, the fixer $6.10 to $8.00: $8.70 to $10.60 | $1.50 to $1.80, 6 to 7 minutes | $0.33 to $0.37, about 2 minutes |

`scores.json` holds the counts the eval gate compares against. K05 was changed after the first grading;
the table above is the second grading, and the first is under "K05, before and after".

## What it means

- **For the first time, the skills separate from the rules, on one finding.** K05 (texts still queued
  when the pod stops are lost) was fixed by all three proven runs and by no other run. Two proven runs
  moved the SMS to an outbox table and one drained its queue on shutdown, as `worker-patterns` and
  `outbox` teach. Every rules-only and no-rules run kept the in-memory channel with no drain. This is the
  round where the task no longer describes the setting: nothing in `task.md` says there are deploys or
  several pods.
- **The other five skill findings didn't separate.** K01, K02, K04 and K06 were fixed by every
  rules-only run, and K03 (HttpClient's own logging of the URI) by two of three. A strong model finds
  them by reading the code.
- **The rules carried the controls, as designed.** K08 (clock skew) was fixed by every run with the
  rules and by no run without them; K07 (unbounded `Notes`) was missed once without them. **But the
  no-rules arm ran on a different model** (see Disclosures), so its gap to the other two arms is not
  the rules' alone. The comparison between with proven and rules only is like for like.
- **One no-rules run broke the service.** no-rules-1 made `CurrentUser` throw when there is no signed-in
  user, so `LabDbContext` can't be made outside a request, and the tests create the schema that way
  (`task.md` says so). Every test failed.
- **This rests on one finding, three runs per arm, and a test changed after the first grading.** It is
  a signal to test on the next fixture, not a settled result.

## K05, before and after

| K05 | First grading | After the change |
|---|---|---|
| with-proven-1 | fail (0 texts) | pass |
| with-proven-2 | fail (4 texts, all the same) | pass |
| with-proven-3 | fail (4 texts, all the same) | pass |
| rules-only-1, 2, 3 | fail (0 texts) | fail (0 texts) |
| no-rules-1 | fail (service doesn't start) | fail (service doesn't start) |
| no-rules-2, 3 | fail (0 texts) | fail (0 texts) |

Why: K05 told the four texts apart by their text, so a correct fix that sends one text with no test name
in it (a test name is health data) failed; and its clock moved a second every 50 ms, so the gateway's
300 ms read as six seconds and a correct sender behind a resilience handler timed out only in the test.

The change was made after the first grading, on K05 only: each result now has its own phone and the
texts are told apart by recipient, and K05 waits on a new `WaitForSlowSmsAsync` (200 ms steps, the same
100 seconds) while R3 keeps `WaitForSmsAsync`. Before regrading, the original still failed every finding
and passed every regression, and both references passed all 14 tests.

## Disclosures

- **The no-rules arm ran on Claude Sonnet 5.5; every other step ran on Claude Opus 5.5.** Found after
  the release, from each run's own usage record (`modelUsage`). No step named a model: the no-rules runs
  took the CLI's default in their empty home, the rules-only runs and the consolidations took Opus in
  theirs, and the reviewers and fixers took it from their agent files (`model: opus`). So no rules
  10/24 against rules only 20/24 measures the model and the rules together. With proven against rules
  only (24/24 and 20/24, K05 the difference) is the same model on both sides. Not rerun: evals are
  paused.
- **Every step ran at medium effort**, the CLI's default, because the clean environment didn't carry
  the session's effort setting. The same for all arms.
- **Each run had the same short list of allowed tools** (read, edit, write, search, `dotnet`, `ls`,
  `mkdir`, `cat`, `find`, `grep`, `rm`, `cp`, and `Skill` for the proven steps). A command outside it
  was refused with nobody to approve it: 3 to 5 refusals per no-rules run, 4 to 7 per rules-only run,
  up to 6 per proven fixer, mostly shell scripts and `.claude/journal.md`. Each run went on another way.

- **The arms ran as one `claude -p` per step this round, not as subagents inside the session** as in
  rounds three and four. The session started in a fresh container, where the harness's agents weren't
  registered and a subagent had no house rules in its context. Each step ran in a clean environment
  (no session, Project or messaging variables) with its own home folder:
  - no rules: an empty home;
  - rules only: the core layer installed with `tools/layer.py` from the `v0.3.5` branch, then
    everything but the 11 rule files removed (no skills, agents, commands or hooks);
  - with proven: the whole core layer, installed the same way. `.claude/` on `v0.3.5` is the same as
    0.3.4. No company layer.

  A probe of each home before the runs confirmed what it loaded, and that `--agent code-reviewer` ran
  as the reviewer. The reviewers ran with `--agent code-reviewer`, `security-reviewer` and
  `dba-reviewer`, the consolidation as a plain session, and the fixer with `--agent tdd-guide`. The
  briefs are the handoff's, word for word. Claude Code's own general skills (code review,
  simplification, security review and the like) were present in all three arms alike.
- **A first set of no-rules runs was discarded.** It ran with the session's own environment, as the
  handoff wrote it, which also gave the runs the Project's tools: the Project's docs name the findings.
  None of the three used those tools, and one posted a progress message to the chat. All three were
  run again in the clean environment above, and those are the ones graded.
- **The first attempt at this round was lost** with its container, before grading. These are the
  second set of runs.
- **K06 was made more robust during that first attempt**, after the reviews had begun and before any
  grading. Some reviewers suggested scoping `GET /results` to its caller, and one rules-only run did, so
  each clinician now reads their own result from their own list. This set of runs started from that
  version, and no other assertion changed then.
- **The usage limit stopped the three proven fixers** about seven minutes in. Each was resumed from its
  own transcript four hours later with "The usage limit stopped you midway. Carry on from where you
  stopped." Nothing else was stopped.
- **K05 was changed after the first grading**, as above.
- **The transcripts were audited** for anything read or written outside each run's folder:
  - No run read the repo, `.git`, `HiddenTests`, a reference copy or the Project, and none ran git.
  - no-rules-1 wrote and ran two helper files (`fix.py`, `new_endpoints.txt`) in the folder above its
    own. They were moved out once it finished, and no other run read them.
  - Two other attempts to write outside were refused by Claude Code, so nothing was written:
    no-rules-2 tried `/tmp`, and no-rules-3 tried the parent folder.
  - The proven fixers first tried `../task.md` one level too high, where no file exists, then read
    their own. One fixer's listing of the parent folder was refused.
  - The proven reviewers and fixers read the harness's skills in their home folder, as their briefs
    allow.
- **Grading here ran on PostgreSQL 16**; CI grades on 17.
