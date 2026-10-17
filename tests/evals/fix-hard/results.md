# Eval results: the fix eval

## 2026-10-16 — first runs (proven-dotnet 0.2.16)

Held out: nothing in the commands, agents or skills was changed before these runs. One run each, so these
are three results, not rates. None of the three could build: there's no NuGet access where they ran.

**How it ran.** Each run fixed its own copy of `ClinicReminders` outside the repo, with the task in
`evals.md`, word for word.
- **With proven:** the known-traps scan, then `code-reviewer`, `security-reviewer` and `dba-reviewer` in
  parallel with its table; the main session consolidated their reports (`runs/with-proven/review.md`), and
  `tdd-guide` applied them, loading the skills each finding cites.
- **Rules only:** one general-purpose agent from the same session, told not to load skills, reviewed and
  fixed.
- **No rules:** `claude -p` from the copy's folder with an empty home folder (checked first: no CLAUDE.md
  or rules in its context). Its report is `runs/no-rules/report.md`.

The copies are in `runs/` exactly as the runs left them; `fix-evals.yml` grades them.

| | With proven | Rules only | No rules |
|---|---|---|---|
| **Findings fixed** (hidden tests) | **13 / 14** | **12 / 14** | **10 / 14** |
| Regressions kept | 5 / 5 | 5 / 5 | 5 / 5 |
| Builds (none could compile where it ran) | yes | yes | yes |
| Missed | H12 | H11, H12 | H10, H11, H12, H14 |
| Work (subagent tokens, wall time) | about 570k: scan, 3 reviewers, fixer; 23 min | about 190k; 12 min | about 280k with cache reads; 3 min |

By finding, everything not listed as missed passed for that run. The token counts come from two different
meters (subagents in the session, `claude -p`'s own usage) and only show the order of magnitude.

## What it means

- **The fixes were better than the findings suggested they'd be.** Three runs that couldn't compile
  anything rewrote a service in 260 to 500 lines each, and all three built and kept every regression. The
  claim-before-send for H13 was right in all three, and so were the time zone, the calendar, the trigger
  and the paging.
- **The rules carry the house bans; the skills carry the tested facts.** No rules missed H10 (it kept
  `EnsureCreatedAsync`) and H14 (the body still picked the patient), the two the always-loaded rules
  state. Rules only missed H11, the transitive Pkcs CVE: the rules ask for zero known vulnerabilities, but
  without a restore only the scan names the package that has one. That
  is the same shape as the review evals: rules first, then a small, tested edge from the skills, at about
  three times the work.
- **All three missed H12, and so did the skills.** The service's own catch was fixed in two runs, but
  `IHttpClientFactory`'s default logging writes the failed request's exception, message and all, and the
  provider's error repeated the number. Nothing in the harness said so. The reference fix ran into it in
  CI; `pii-masking` 1.5.0 now has it, tested.

**The scan, run again on the fixed copies,** names the misses it knows: the Pkcs pin in rules only and
no rules, and `EnsureCreatedAsync` in no rules. It's clean on with-proven, after one false positive was
fixed: a start-up check that `ar-SA` exists (`_ = GetCultureInfo("ar-SA", predefinedOnly: true)`) isn't
formatting with it. H12 and H14 aren't patterns a text scan can see. So a scan after the fix is a cheap
second gate for half of what slipped through.

## What the eval found in the harness

- `pii-masking`: the "`UseSerilog()` replaces the factory" story was half the truth. Tested in a real host
  (`SerilogOrderTests`): with `EnableRedaction()` after `UseSerilog()`, as in this fixture, nothing logged
  through `ILogger` reaches Serilog at all; only the other order drops the field. The skill's own setup
  line still showed `UseSerilog()`; it now shows Serilog as a provider. The code reviewer also suggested
  `builder.Services.AddSerilog()` "as a provider", which is the factory again; the skill now names it.
- `known-traps` 1.2.0: `serilog-factory-with-redaction`, and no `ar-sa-default-calendar` finding on a check
  that the culture exists.
- `pii-masking` 1.5.0: `HttpClient` logging and exception text, above.

## The hidden tests themselves

Two came out wrong and were fixed, each from the original or the reference, before or alongside the runs:
- **H08** first passed on the original for the wrong reason, matching a logged `CREATE TABLE` that has a
  `SentAt` column, and then because the line did reach the logger factory's providers. It now requires
  the line at Serilog's sink, which the original never sends there.
- **H06** threw on a copy with no job store configured: Quartz's options have no `quartz.jobStore.type`
  key then, rather than a null one, and the reference and the original both configure a store, so neither
  reached that path. It failed with-proven and no-rules (both moved to the in-memory store, which the test
  accepts) until the test read the missing key as the in-memory default. The numbers above are after
  that fix. Lesson for the next fixture: the reference should take each fix the tests accept, not one.

**The order of the commits.** The copies went into the repo in the same commit as the H08 change, and
the H06 bug was found from the runs' results. No test was changed to move a run's score in either
direction: H08 changed because of the original, and H06 now does what `evals.md` said it does.

## 2026-10-17 — round two: builds allowed

Same fixture, same task, except each run could `dotnet build` (NuGet reachable) and write and run unit
tests; still no SQL Server or Docker. With proven ran on **0.2.16**, from a checkout of that commit, so
H08 and H12 stayed held out: 0.2.17's `pii-masking` and `known-traps` were written from round one. A second
reference fix on the in-memory Quartz store (`reference-memory/`) now checks H06's other accepted form;
both references pass everything and the original fails every finding.

| | With proven 0.2.16 | Rules only | No rules |
|---|---|---|---|
| **Findings fixed, round two** | **13 / 14** | **14 / 14** | **6 / 14** |
| Round one, for comparison | 13 / 14 | 12 / 14 | 10 / 14 |
| Regressions kept | 5 / 5 | 5 / 5 | 5 / 5 |
| Failed | H12 | none | H02, H03, H08, H10, H11, H12, H13, H14 |
| Its own unit tests | 61, all pass | 42, all pass | 3, all pass |
| Work | about 620k tokens: 3 reviewers, fixer; 24 min | about 185k; 14 min | about 430k with cache reads; 3 min |

**What it means**

- **One run per arm is noise.** No rules went from 10 to 6 with a compiler in hand, rules only from 12 to
  14. The same fixture and task, one run each, swings by four findings. The two rounds together don't
  show that the skills fix more than the rules alone; they show both are far above no rules, most of the
  time, and that the next eval needs several runs per arm before a one-point gap means anything.
- **With proven was the steady one: 13 both times, the same miss.** The scan put the Pkcs pin, invariant
  mode, the singleton and the Quartz setting in front of the fixer every time, and those never slipped.
  What varies between runs is what a model happens to notice; what a scan names doesn't vary.
- **Rules only's H12 pass wasn't aimed at H12.** Its `appsettings.json` keeps
  `System.Net.Http.HttpClient` at `Warning` in Serilog's overrides, a common way to cut noise, and the
  factory's "request failed" line is `Information`, so the exception text never reached a sink. The
  hidden test grades behavior, so it counts, but its report says nothing about exception text. It did
  find, with a throwaway project, that `EnableRedaction()` replaced Serilog's factory, which is the
  0.2.17 `pii-masking` finding reached on its own.
- **No rules, with the compiler, saw more and fixed less.** It saw the Pkcs advisory, tried 10.0.0,
  found that flagged too and left it ("look at it when a patched version ships"; 10.0.13 is the fix). It
  noted that invariant mode breaks Arabic and kept it. It saved after each send instead of claiming
  first, so a crash after the send sent again (H13).
- **H12 is still the one the skills at 0.2.16 don't teach.** With proven took the exception out of its
  own log line both times; the factory's logging kept writing it. 0.2.17 covers it; the next round on
  0.2.17 will show whether it holds, but it won't be held out.

## Next

- ~~A second reference form for H06~~ (done in round two, `reference-memory/`). Still to do: one for H13
  (an outbox or a status column instead of a claim timestamp).
- ~~Run it with the builds allowed~~ (round two).
- **Three runs per arm**, on a fixture the skills haven't learned from, before reading a gap of one or two
  findings as a difference.
