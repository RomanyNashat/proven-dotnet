# The fix eval: does the fix work?

The review evals score what a review finds, graded by a judge. A finding is cheap; the work is a fix that
is right and breaks nothing. Here each run reviews `ClinicReminders` and fixes it, and the hidden tests
decide. No judge, no partial credit for a good description of a wrong fix.

All cases are **held out**: nothing in the commands, agents or skills was changed to pass them.

## The task every run gets

Word for word, the same for all three runs (in `results.md` with each run's notes):

> Review the .NET service in this folder (`ClinicReminders/`) and fix what's wrong, as you would before
> it goes to production. Edit the files in place. You can't build or run it here (there's no access to
> NuGet), so read carefully and make each change compile. Tests you can't see will run the service
> in-process with `WebApplicationFactory<Program>`, in the Production environment, against SQL Server.
> They rely on these, so keep them working: `Program` and the three routes; `RemindersDbContext` (the
> tests create the schema from its model); `SendDueRemindersJob`, built from DI and run with `Execute`;
> `SmsClient` as a typed `HttpClient`; `TimeProvider` from DI; `ConnectionStrings:Reminders`; Serilog from
> the `Serilog` section. The SMS provider has no idempotency key. The service runs in Riyadh; patients
> read the SMS in Arabic.

The contract says what the tests touch, never what they check. A run that can't build is the same
handicap for all three; a fix that doesn't compile fails every test, as it would in a pipeline.

## The hidden tests

| Test | Finding | Passes when |
|---|---|---|
| H01 | a singleton holding the scoped `DbContext` | the host builds with `ValidateScopes` and `ValidateOnBuild` on |
| H02 | invariant mode in the image of a SqlClient service | the final stage doesn't set invariant mode, and an Alpine image adds `icu-libs` (chiseled only as `-extra`) |
| H03 | `FindSystemTimeZoneById("Asia/Riyadh")` with no fallback | with no time-zone database, the SMS still says 09:30 for 06:30 UTC |
| H04 | `ar-SA` formats the date the patient reads | on an ICU whose `ar-SA` is Hijri, the SMS shows 2026, not 1448 |
| H05 | the 9 a.m. trigger with no time zone | the cron trigger's next fire after midnight UTC is 06:00 UTC |
| H06 | a persistent store that drops a run missed during a deploy | `ScheduleTriggerRelativeToReplacedTrigger`, or no persistent store |
| H07 | a base address without a trailing `/` | the SMS goes to `https://sms.example.net/api/v2/messages` |
| H08 | `UseSerilog()` and `EnableRedaction()` together | the "sent" line reaches Serilog's sink; if its template still names `{Mobile}`, the field is there, redacted; the number appears nowhere |
| H09 | keyset paging on `CreatedAt` alone | paging through 25 reminders created in one instant returns all 25 |
| H10 | `EnsureCreatedAsync()` at startup | starting the service leaves an empty database empty |
| H11 | a transitive vulnerable package (Pkcs via SqlClient) | a NuGet audit of the restore (all severities, transitive) is clean |
| H12 | the provider's exception, which echoes the number, is logged | a failed send is still logged at warning or error, and the number appears in no log line, whoever wrote it |
| H13 | the SMS is sent before the save | a crash on the first write after the send, then a second run: one SMS |
| H14 | `PatientId` from the body | patient 1 posting for patient 2 leaves no reminder for patient 2 |

Regressions, which pass on the original and must still pass: R1 a patient schedules and lists their
reminder; R2 a due reminder sends one SMS with 09:30 and the mobile; R3 a later appointment isn't sent;
R4 `/health` is open; R5 `/reminders` needs a signed-in user.

H08 turned out different from what the hard fixture said. There, `UseSerilog()` comes first and
`EnableRedaction()` a line later, and in a real host the second puts its own logger factory back over
Serilog's: the line is redacted, but it reaches no Serilog sink at all, so the service's logs stop
arriving. (In the other order Serilog's factory wins and the classified field vanishes.) Either way the
fix is the same, Serilog as a provider; the test checks the line arrives at Serilog, redacted.
`SerilogOrderTests` in the skill samples shows both orders.

H12 has a second writer the hard fixture didn't plant: `IHttpClientFactory`'s own logging writes "HTTP request failed" with the exception, message and all. The reference fix found it in CI and removes that client's loggers (`pii-masking` 1.5.0 now has the test).

`fix-evals.yml` fails if the original passes any finding test or the reference fails any test, so a test
that can't tell a fix from no fix is caught before it scores a run.

## Runs

- **With proven:** `/full-review` as it runs (the known-traps scan, then `code-reviewer`,
  `security-reviewer` and `dba-reviewer` in parallel with the scan's table, each loading its skills), then
  `tdd-guide` applies the findings, loading the skills they cite.
- **Rules only:** one general-purpose agent from the same session (the house rules in its context), told
  not to load skills, does both.
- **No rules:** `claude -p` from a folder outside the repo with an empty home folder.

Each fixes its own copy outside the repo. The copies go to `runs/<name>/` unchanged, and CI grades them.

## Scoring

Findings fixed out of 14, regressions kept out of 5, and whether it builds. A run that breaks a
regression has made the service worse somewhere, which counts against it as much as a missed finding.
