# The fix eval, round five: a task that doesn't hand over the setting

## The hidden tests

| Test | Finding | Passes when | Where it's taught |
|---|---|---|---|
| K01 | `RoleClaimType = "realm_access.roles"`: Keycloak's roles sit inside a JSON claim, so `RequireRole` never sees them | a clinician with a Keycloak-shaped token gets 200 | `auth-patterns` |
| K02 | keyset paging on `ReceivedAt` alone: results saved in the same instant are skipped | five results with one timestamp all come back, two per page | `efcore-patterns`, `api-design` |
| K03 | HttpClient's own logging writes the request URI, and the lab's URI carries the national ID | a lookup logs no national ID | `pii-masking` |
| K04 | the national-ID mask matches `[0-9]`, so an ID typed in Arabic-Indic digits is logged whole | an Arabic-Indic national ID isn't in the logs, in either digits (a 400 is fine) | `pii-masking` |
| K05 | the SMS queue is in memory and its loop stops on the shutdown token: a deploy drops what's queued | four texts queued when the pod stops are all sent (by it, or by the next pod) | `worker-patterns` |
| K06 | the audit interceptor sits in a pooled context's options with a user read once: every row gets one user | each result's `recordedBy` is the clinician who recorded it | `efcore-patterns` |
| K07 | `Notes` has no length, so it's `text` | every string property has a max length of at most 4000 | rules (`efcore-rules.md`) |
| K08 | the default 5-minute clock skew | a token that expired two minutes ago gets 401 | rules (`security.md`) |

Regressions, which pass on the original and must still pass: R1 a clinician records a result and finds it
in the list; R2 results at different times come back newest first across pages; R3 a recorded result
texts the patient once; R4 a patient's token gets 403; R5 no token is 401 and health is open; R6 the lab
lookup returns the lab's orders, calling `https://labs.example.net/api/patients/{nationalId}/orders`.

The tokens the tests sign carry the roles both as Keycloak does (inside `realm_access`) and as a plain
`realm_access.roles` claim, so only K01 depends on how the service reads Keycloak's roles. While a test
waits for an SMS the tests' clock moves on, so a worker that polls on the injected `TimeProvider` gets its
ticks. K06 reads each clinician's result from that clinician's own list, so a list scoped to its caller
passes too.

`fix-evals.yml` fails if the original passes any finding test or either reference fails any test.

## Runs

As round four: the task is `task.md`, word for word, each run fixes its own copy outside the repo, and
there is no PostgreSQL or Docker for the runs.
- **With proven:** the known-traps scan, then `code-reviewer`, `security-reviewer` and `dba-reviewer` in
  parallel with the scan's table, consolidated into one review by a general-purpose agent, then
  `tdd-guide` applies it, loading the skills each finding cites. The main session starts each agent.
- **Rules only:** one general-purpose agent with the house rules in its context, told not to load skills.
- **No rules:** `claude -p` from the copy's folder with an empty home folder.

Three runs per arm, `runs/<arm>-<n>/`. Scored per finding: how many of the three runs fixed it.
