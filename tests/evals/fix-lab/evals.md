# The fix eval, round three: a new service, three runs per arm

Round two showed one run per arm swings by four findings. This round runs each arm three times on a
service none of the skills were written against, so a difference has to hold across runs to count.

## The hidden tests

| Test | Finding | Passes when | Where it's taught |
|---|---|---|---|
| H01 | a `DateTime` from JSON with an offset is `Kind=Local`; Npgsql refuses it for `timestamptz` | a reading sent at `08:00+03:00` is stored and returned as 05:00 UTC | `postgresql-patterns`, `efcore-patterns` |
| H02 | `AddStandardResilienceHandler()` retries POSTs | a 503 from the care team is followed by no second POST in the same run | `polly-resilience` |
| H03 | one failing alert stops the notifier's whole batch | with one alert always refused (400), the next one still goes out within three runs | `worker-patterns` |
| H04 | the "latest reading" cache key is the same for every patient | patient 2 never sees patient 1's latest reading | rules (ownership), `caching` |
| H05 | `GET /readings/{id}` doesn't check the owner | patient 2 gets 404 or 403 for patient 1's reading | rules (`security.md`) |
| H06 | `x-language: ar` sets `CurrentCulture`, and `decimal.Parse` then refuses `5.4` | an Arabic app's `5.4` is stored as 5.4 | `localization`, rules (invariant culture) |
| H07 | "today" is the UTC day | at 01:30 in Riyadh, today's chart has 00:30 and not 23:30 the night before | `cronjob-patterns`, `localization` |
| H08 | liveness checks the database | with the database down, `/health/live` is 200 and `/health/ready` isn't | `kubernetes-dotnet` |
| H09 | the photo is saved under the client's file name | `../../../x.jpg` writes nothing outside the photo folder | rules (uploads) |
| H10 | `{@Reading}` logs the whole entity, national ID included | the national ID is in no log line | `pii-masking`, rules |
| H11 | the default 5-minute clock skew | a token that expired two minutes ago gets 401 | rules (`ClockSkew` 30 s) |
| H12 | the error middleware returns the exception's message | with the database down, a 5xx body names no host, driver or exception | rules (ProblemDetails) |
| H13 | `Notes` has no length, so it's `text` | every string property has a max length of at most 4000 | rules (`efcore-rules.md`) |

Regressions, which pass on the original and must still pass: R1 a patient saves a reading and reads it
back; R2 today's chart has this morning's reading; R3 a high reading reaches the care team once, at
`https://careteam.example.net/api/alerts`; R4 both health routes are open and up with the database up;
R5 readings need a token; R6 the Arabic app gets its message in Arabic.

Five findings are taught only by the skills (H01, H02, H03, H07, H08), five only by the always-loaded
rules (H05, H09, H11, H12, H13), and three by both (H04, H06, H10). So the arms can differ where they
should: the rules against no rules, and the skills against the rules alone. (H01's rule, "store UTC",
names the goal but not that `Kind=Local` is what breaks it.)

`fix-evals.yml` fails if the original passes any finding test or either reference fails any test.

## Runs

The task is `task.md`, word for word, for every run; each fixes its own copy outside the repo. Builds
are allowed (NuGet is reachable); there is no PostgreSQL or Docker.
- **With proven:** the known-traps scan, then `code-reviewer`, `security-reviewer` and `dba-reviewer` in
  parallel with the scan's table, consolidated into one review, then `tdd-guide` applies it, loading
  the skills each finding cites.
- **Rules only:** one general-purpose agent with the house rules in its context, told not to load skills.
- **No rules:** `claude -p` from the copy's folder with an empty home folder.

Three runs per arm, `runs/<arm>-<n>/`. Scored per finding: how many of the three runs fixed it.
