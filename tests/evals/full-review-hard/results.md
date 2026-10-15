# Eval results: the harder fixture

## 2026-10-15 — first runs (proven-dotnet 0.2.9)

Held out: nothing in the commands, agents or skills was changed before these runs. One run each, so these
are two results, not rates.

**How it ran.** Each run reviewed a copy of `ClinicReminders` outside the repo, without `evals.md` beside
it. Nothing restored or built (no NuGet access).
- **With proven:** `/full-review`'s three reviewers (`code-reviewer`, `security-reviewer`, `dba-reviewer`)
  launched in parallel from the main session, each told to use its skills.
- **Baseline:** one general-purpose reviewer told not to use skills or `proven-roslyn` and not to read
  anything outside the fixture. It was started from the same session, so it **had the house rules and
  `core.md` in context**: rules only, no commands, agents or skills (`skill-evals` point 7). A baseline
  with no rules at all needs a session outside the repo.
- **Grading:** a separate agent that hadn't seen the runs made, given the answer key and the two reports
  as "A" and "B" without saying which was which. Every verdict quotes the report.

| | With proven (3 reviewers) | Baseline (rules only) | Difference |
|---|---|---|---|
| Planted findings (found = 1, partial = 0.5) | **11.5 / 14** | **10.5 / 14** | +1 |
| Case 1 (H1, H2, H3, H5, H6, H7) | 5 / 6 | 5 / 6 | 0 |
| Distractors wrongly flagged | 0 / 5 | 0 / 5 | 0 |
| Severities within one level | all | all | — |
| Fixture unchanged | pass | pass | — |
| Tokens (review only) | 218k | 139k | +57% |

**Where they differed:** only H11. With proven, the security reviewer named the transitive
`System.Security.Cryptography.Pkcs` vulnerability through SqlClient, both CVEs and the 10.0.13 pin, and
said it couldn't confirm with a tool. The baseline said it hadn't checked.

**Missed by both:**
- **H6**, the Quartz misfire setting. Both reviewed the persistent store (clustering, missing tables, DBA
  approval) and neither knew `ScheduleTriggerRelativeToReplacedTrigger`. The reviewers didn't load
  `quartz-scheduling`, where it is.
- **H12**, the exception text in `LogError(ex, ...)`. Both cleared the line as safe ("logs only the
  reminder id"). `pii-masking` says masking doesn't touch the exception; the reviewers didn't apply it.
- **H8** half-found by both: both saw that `UseSerilog()` and `EnableRedaction()` fight over the logger
  factory and gave the right fix, but neither said what actually happens (the classified field
  disappears from the line).

**Real defects found by both that aren't planted:** the missing JwtBearer package (build fails),
`IHttpContextAccessor` never registered (every request and the job fail), `MapInboundClaims` renaming
`sub`, the Quartz store not clustered and its tables never created, a due-window with no lower bound,
no request validation, the paging index not leading with `PatientId`.

## What it means

- **The rules do most of the work.** The baseline, with the house rules and `core.md` but no skills or
  agents, found 10.5 of 14 findings that were chosen because the skills' tests found them. Several of the
  October findings are now in the rules themselves (ICU and SqlClient, `ar-SA`'s calendar, the trailing
  `/`, the singleton holding a scoped service), so a rules-only run knows them. That's the rules earning
  their place, and it makes "with vs without skills" a small gap by design.
- **The skills earned one finding here, at 57% more tokens**, and the gain came from three reviewers in
  parallel, each going deeper on its own area (the DBA reviewer's index and claim-batch advice, the
  security reviewer's dependency check).
- **The two misses are reviewers not reaching for a skill they have.** Fixed in this change, and
  therefore no longer held out: `dba-reviewer` has a scheduler checklist (misfire setting, `InTimeZone`,
  clustering), and `security-reviewer` checks exception text and Serilog-as-provider. The next run of H6,
  H8 and H12 measures those lines, not the skills.
- **Next:** a baseline from a session outside the repo (no rules), to measure the rules themselves; and a
  fixture area the rules don't cover, to measure the skills alone.
