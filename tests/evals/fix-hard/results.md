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

## What the eval found in the harness

- `pii-masking`: the "`UseSerilog()` replaces the factory" story was half the truth. Tested in a real host
  (`SerilogOrderTests`): with `EnableRedaction()` after `UseSerilog()`, as in this fixture, nothing logged
  through `ILogger` reaches Serilog at all; only the other order drops the field. The skill's own setup
  line still showed `UseSerilog()`; it now shows Serilog as a provider. The code reviewer also suggested
  `builder.Services.AddSerilog()` "as a provider", which is the factory again; the skill now names it.
- `known-traps` 1.2.0: `serilog-factory-with-redaction`.
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

## Next

- Give the reference a second form for H06 and H13 (an in-memory store; an outbox), so a test that can't
  accept a correct fix is caught before a run meets it.
- Run it again with the builds allowed: the gap a compiler closes is probably larger than the gap between
  the three runs here.
