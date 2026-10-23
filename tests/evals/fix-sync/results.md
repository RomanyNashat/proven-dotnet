# Eval results: the fix eval, round four

## 2026-10-23: findings the rules don't name, three runs per arm (proven-dotnet 0.3.2)

Held out: `MeterSync` was written after the skills, and nothing in the harness changed for it. Each run
fixed its own copy outside the repo, with `task.md` word for word. Builds were allowed; PostgreSQL and
Docker weren't there (the local server was stopped for the whole run). The copies are in `runs/` as the
runs left them, with their reports and reviews, and `fix-evals.yml` grades them.

| | With proven | Rules only | No rules |
|---|---|---|---|
| **Findings fixed** (3 runs × 10) | **30 / 30** | **30 / 30** | **27 / 30** |
| Regressions kept (3 runs × 6) | 18 / 18 | 18 / 18 | 18 / 18 |
| Builds | 3 / 3 | 3 / 3 | 3 / 3 |
| Missed | none | none | M10 in two runs, M02 in one |
| Test methods it added | 58, 59, 40 | 30, 25, 29 | 4, 3, 0 |
| Work per run | 3 reviewers about 90k tokens each, a consolidation about 130k, the fixer about 210k to 260k | about 180k to 190k | about $1.40 with cache reads, 3 minutes |

`scores.json` holds the counts the eval gate compares against.

## What it means

- **The skills still don't separate from the rules.** Five findings were picked because only the skills
  teach them: the base address that drops `v2` (M01), the time zone missing on a slim image (M02), two
  pods sending the same SMS (M04), the audience left unchecked (M07), CORS that grants any site (M08).
  Rules only fixed all five, in every run. So did no rules, except M02 once. A strong model that reads
  `task.md` and `VENDOR.md` closely finds them: the task names the production base address without its
  slash, says the service runs on several pods, and says patients are in Riyadh.
- **The rules carried the one gap that showed.** No rules missed the singleton holding the `DbContext`
  (M10) in two of three runs, the finding `rules/csharp-standards.md` names ("never inject Scoped into
  Singleton"). Round three's no-rules misses were also the rules' house numbers. Both rounds point the
  same way: what moves the score is the always-loaded rules, not the skills.
- **The domain findings weren't hard either.** HI and LO (M05) and mmol/L (M06) were fixed by every run;
  `VENDOR.md` states both.
- **The proven arm costs the most and writes the most tests** (about twice the rules-only arm's), and
  its reviews found more than the hidden tests measure: a meter's whole history texted when it's
  linked, one bad row blocking every alert behind it, an SMS timeout stopping the host, serial numbers
  that differ only in case, the vendor's paths reachable through a serial of `..`. None is graded.

## What the harness gets from this

The reviewers and all three fixers named problems in the harness itself, which is what this round
can show that the score can't:
- **`worker-patterns` has a bug in its samples.** `catch (Exception ex) when (ex is not
  OperationCanceledException)` lets an `HttpClient` timeout through (a `TaskCanceledException` is an
  `OperationCanceledException`), so the worker faults and the host stops. The filter should be
  `when (!stoppingToken.IsCancellationRequested)`. Raised by two code reviews and all three proven fixers.
- **The known-traps scan missed `new("ar-SA")`:** `ar-sa-default-calendar` doesn't catch the
  target-typed form. Three reviewers found it themselves.
- **A body field naming someone else's record isn't scanned for** (the national ID in `POST /meters`).
  Every reviewer found it by reading; a text scan can't tell whose ID it is, so this stays a review item.
These go through `/lessons`, with the gate on fix-lab, fix-hard and this fixture.

## Disclosures

- **The hidden tests' clock changed after the runs, before grading.** They linked the meter at 12:00
  and synced readings taken at 10:00 the same day. Three fixers chose not to text readings taken before
  the meter was linked (a reasonable answer to the history finding the reviewers raised), so a correct fix
  failed. The meter is now linked at 08:00, the readings come after it, the sync runs at 12:00, and two
  pods share one clock. No assertion changed. The original still fails every finding and passes every
  regression, and both references pass everything.
- **The usage limit stopped most runs midway.** Two no-rules runs, the three rules-only runs, all nine
  reviewers and the three fixers were stopped and resumed from their own transcripts with "carry on from
  where you stopped". Token counts above are what the agents reported after resuming, so they're low.
- **The proven pipeline was driven from the main session.** A subagent can't start agents here, so a
  first attempt with one orchestrating agent per run did nothing. The scan was run once and copied to
  each run, the three reviewers ran as separate agents, a general-purpose agent consolidated their
  reports into `review.md`, and `tdd-guide` applied it.
- **No run could write `report.md`.** Subagents are refused report files here; each report was saved
  from the agent's final message. The no-rules runs' reports are their final answers.
- **Shared scratch space.** One proven fixer noted another run's files in the session's scratch folder
  overwrote its own working folder there; both kept their service changes inside their own run folder.
