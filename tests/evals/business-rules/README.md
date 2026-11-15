# The business-rules eval: does it find the rules the code enforces, and only those?

A run gets `Referrals/`, a service whose rules are spread over its layers: a controller and its request's
DataAnnotations, a service, an entity, a repository's query, and a setting. It also holds five things that
look like rules and aren't enforced. The run writes `Referrals/rules.md` (`task.md`). Both arms get the
same task; the harness arm is also sent to the `business-rules` skill and its script (`route.txt`).

**How a copy is graded** (`tools/grade_one.py`, against `key.json`), from the R lines of `rules.md`:
- **K1–K6, the enforced rules:** pass when an R line names one of the rule's lines.
- **D1–D5, the decoys:** pass when no R line names the decoy's lines or describes it (its words), unless
  that same line says it isn't enforced.

| Key | What | Where |
|---|---|---|
| K1 | only a treating doctor may refer; anyone else gets NotFound | `ReferralDesk` guard |
| K2 | a second referral to a clinic with one open returns the open one | `ReferralDesk` guard, repository |
| K3 | a reason is required and at most 500 characters (400) | the request's DataAnnotations under `[ApiController]` |
| K4 | only an open referral can be accepted (409) | `ReferralDesk`, `Referral.Accept()` |
| K5 | a clinic's queue shows open referrals of the last 90 days | the repository's `Where` |
| K6 | at most `MaxPerPatientPerDay` (3) referrals a patient per UTC day (429) | `ReferralDesk`, `appsettings.json` |
| D1 | patients over 65 skip the queue | a comment only |
| D2 | a 10% discount from the fifth visit | `FeeCalculator`, never called |
| D3 | a daily cap of 5 | `MaxDailyReferrals`, never used |
| D4 | a referral expires after 30 days | the README only |
| D5 | a note is required, a reason at least 10 characters | a FluentValidation validator that is never registered |

`summary.json` also reports, ungraded, the R lines and whether the skill's `--check` accepts the file
(the rules-only arm never sees the script).

| Copy | Rules found | Decoys not claimed |
|---|---|---|
| `original` (no rules.md) | 0 / 6 | 5 / 5 |
| `reference` (the skill's `--check` accepts it: 20 of 20 candidates) | 6 / 6 | 5 / 5 |

## The bar, written before any run (2026-11-15)

- **Arms:** rules only and the harness (`route.txt`). `claude-opus-5-5` at medium.
- **Pilot first:** one rules-only run. If it finds all six rules and claims none of the decoys, the fixture
  can't tell the arms apart: report that and stop, as `fix-qa` and `story-check` did, rather than redesign
  after reading the run.
- **Otherwise three runs per arm, and the noise floor once. Win**, averaged, beyond the noise: the harness
  claims at least 1 fewer decoy per run and finds no fewer rules, or finds at least 1 more rule per run and
  claims no more decoys.
- **Falsifier:** neither.

## The pilot (2026-11-15): it found everything, so the grid wasn't run

One rules-only run (`claude-opus-5-5`, medium, commit `55e5096`): $0.47, 18 turns, 88 seconds. Its
`rules.md` is in `runs/rules-1`.

- **6 of 6 rules found, 0 of 5 decoys claimed.** Each decoy went under "Not enforced" with the reason: the
  comment, the README, the unused constant, the validator that is never registered, the calculator that is
  never called.
- **It found more than the key.** The `[Range(1, 20)]` on `MaxPerPatientPerDay` isn't enforced either:
  `ValidateOnStart()` is there but `ValidateDataAnnotations()` isn't, so a missing setting is 0 and every
  referral answers 429. The first `reference` claimed that range as part of R6; it was wrong, and it was
  fixed after this run, as was `K6`'s list of lines (the `[Range]` line no longer counts as finding it).
  It also noted that `Program.cs` registers no `ReferralsDb` or `IPatientDirectory`, that the duplicate
  and the daily-cap checks race under two requests at once, and that nothing checks who calls (any caller
  accepts any referral).
- **The skill's `--check` doesn't accept its file**, as expected: its references have no backticked code.

So, as the bar said, it stops here rather than being redesigned after reading the run (as with `fix-qa`
and `story-check`). Three fixtures in a row now say the same thing: on a service this size,
`claude-opus-5-5` at medium does this work without the skill. What the skill adds is the part this eval
can't score: every candidate line accounted for, references that survive lines moving, and the tests that
pin each rule (this task asked for none).
