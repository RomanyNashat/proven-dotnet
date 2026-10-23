# The fix eval, round four: findings the rules don't teach

Round three's rules-only arm fixed everything the skills teach, so it couldn't tell the skills from the
rules. This round's findings were picked so the arms can differ: five come from what the skills' own
tests found in real libraries and no rule names, two are the rules', one is both, and two are domain
judgment the service's own docs make visible.

## The hidden tests

| Test | Finding | Passes when | Where it's taught |
|---|---|---|---|
| M01 | the vendor's base address `…/v2` has no trailing `/`, so every relative path drops `v2` | the vendor is asked at `/v2/meters/{serial}/readings` | `dotnet-core` |
| M02 | `FindSystemTimeZoneById("Asia/Riyadh")` with no fallback throws on an image without tzdata | with an empty `TZDIR`, the SMS still goes out, at 13:00 for a 10:00 UTC reading | `localization` |
| M03 | the SMS formats the date with `ar-SA`, whose calendar is Hijri under Ubuntu 24.04's ICU | the SMS says 2026, not 1448 | `localization`, rules (ar-SA) |
| M04 | every pod sweeps the same unsent alerts | two pods sweeping at once send each of three alerts once | `worker-patterns` |
| M05 | a `HI` or `LO` reading isn't a number, so it's skipped | `LO` and `HI` each text the patient | domain (`VENDOR.md`) |
| M06 | an mmol/L value is compared with the mg/dL thresholds | 3.0 and 15.0 mmol/L text the patient, 5.5 doesn't | domain (`VENDOR.md`) |
| M07 | `ValidateAudience = false` | a token issued for another service gets 401 | `auth-patterns` |
| M08 | CORS allows any origin, with credentials | a site that isn't the clinic dashboard gets no `Access-Control-Allow-Origin` | `owasp-aspnetcore` |
| M09 | `POST /meters` links the meter to whoever the body's national ID names | patient 1 can't link a meter to patient 2 | rules (`security.md`) |
| M10 | `PatientDirectory` is a singleton holding the scoped `DbContext` | 60 concurrent `GET /meters` all get 200 | rules (`csharp-standards.md`), `dotnet-core` |

Regressions, which pass on the original and must still pass: R1 a patient links a meter and sees it; R2
a low reading texts the patient once, at the gateway's `/messages`, and a normal one doesn't; R3 a reading
synced twice is texted once; R4 the clinic dashboard gets its CORS grant; R5 the API needs a token and
takes one for `meter-sync`; R6 health is open.

`fix-evals.yml` fails if the original passes any finding test or either reference fails any test.

## Runs

As round three: the task is `task.md`, word for word, each run fixes its own copy outside the repo, and
there is no PostgreSQL or Docker for the runs.
- **With proven:** the known-traps scan, then `code-reviewer`, `security-reviewer` and `dba-reviewer` in
  parallel with the scan's table, consolidated into one review by a general-purpose agent, then
  `tdd-guide` applies it, loading the skills each finding cites. The main session starts each agent (a
  subagent can't start agents).
- **Rules only:** one general-purpose agent with the house rules in its context, told not to load skills.
- **No rules:** `claude -p` from the copy's folder with an empty home folder.

Three runs per arm, `runs/<arm>-<n>/`. Scored per finding: how many of the three runs fixed it.
