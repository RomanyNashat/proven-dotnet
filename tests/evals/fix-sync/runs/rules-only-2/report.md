I fixed 16 problems in MeterSync and added 25 unit test methods (49 cases counting theory rows). All of them pass and the service builds with no warnings. Neither project has a vulnerable package, direct or transitive. The tool blocked writing `report.md` ("subagents should return findings as text"), so the full list is here instead.

The parts that need PostgreSQL never ran here: the two-pod sync, the alert claim race and the duplicate-link 409. They are only built, and are left for your integration tests.

## Fixed

1. **`MeterSync/Program.cs`: vendor calls dropped `/v2`.** `MeterVendor:BaseAddress` has no trailing slash, so `meters/{sn}/readings` went to `https://meters.example.net/meters/...`. The new `MeterSync/Infrastructure/BaseAddress.cs` adds the slash (the setting stays as production has it) and fails clearly if the setting is missing. Both typed clients use it.
2. **`Program.cs`: `PatientDirectory` was a singleton holding the scoped `SyncDbContext`.** In Production one DbContext was shared by every request, so concurrent `GET /meters` calls could throw. It is now scoped, and lifetime checks (`ValidateScopes`, `ValidateOnBuild`) are on in every environment.
3. **`Program.cs`: the token audience wasn't checked** (`ValidateAudience = false`), so a token from the same identity provider for any other app was accepted. It now requires `meter-sync`. The settings are changed on the existing token options rather than replacing them, so a signing key set elsewhere survives.
4. **`Program.cs`: CORS allowed every origin, with credentials.** Any website could call the API as the signed-in patient. It now allows only `Cors:Origins`, GET/POST and the `Authorization`/`Content-Type` headers. Credentials are off, since the token travels in a header.
5. **`Program.cs`: no exception handler.** Added ProblemDetails and `UseExceptionHandler()`, so errors don't expose internals.
6. **`Program.cs`: the SMS client had the default 100 s timeout.** It is now 30 s, inside the alert claim lease (item 12). It still has no retry handler, on purpose: the gateway has no idempotency key, so a retried POST is a second text.
7. **`MeterSync/Api/MeterEndpoints.cs`: any signed-in user could link a meter to any national ID.** That could send one patient texts about someone else's readings, or hand a victim's meter and its alerts to an attacker. The patient must now be the caller (`sub`). Someone else's national ID gets 404, the same as an unknown one, so the endpoint can't reveal which national IDs are registered. `NationalId` is validated too.
8. **`MeterEndpoints.cs`: two links of the same serial number at once gave a 500.** The unique-index violation now returns 409.
9. **`MeterSync/Api/CurrentPatient.cs`: a missing or non-numeric `sub` caused a 500.** It now returns null and the endpoints answer 401.
10. **`MeterSync/Application/MeterSyncService.cs` and new `MeterSync/Domain/GlucoseValue.cs`: values were read wrongly.**
    - `HI` (over 600 mg/dL) and `LO` (under 20) were skipped as "not a number", so the most dangerous readings never alerted. They now map to 600 and 20 and alert High and Low.
    - `mmol/L` values were treated as mg/dL. A normal 6 mmol/L was texted as low, and 15 mmol/L as low instead of high. They are now converted (×18), and an unknown unit is skipped.
    - Thousands separators were accepted (`1,12` became 112). Only a plain decimal is accepted now.
    - A field longer than its column is skipped instead of failing the save.
    - The skipped reading's value (health data) is no longer logged.
11. **`MeterSyncService.cs`: one failure stopped everything, and two pods collided.**
    - A single save covered all meters, so one vendor 404 or timeout lost every patient's readings for that run. It now saves per meter, and one meter's failure is logged without stopping the rest.
    - Two pods syncing at once hit the unique `VendorId` index. That race is now retried against what the other pod saved, up to 3 attempts.
    - The duplicate check now looks up only the IDs in the vendor response, not every reading the meter ever had.
12. **`MeterSync/Application/AlertSweeper.cs` and `MeterSync/Domain/Reading.cs`: duplicate texts and blocked alerts.**
    - Every pod sent the same pending alerts, so patients got one SMS per pod. Each reading is now claimed with a conditional UPDATE (new `AlertClaimedAt` column, 5-minute lease) before sending, and only the pod that wins sends.
    - One failing SMS threw on every run and blocked every alert behind it. A failed send now keeps its claim and is retried after the lease, while the rest go out.
    - A missing patient threw `KeyNotFoundException`; patients are now joined in the query.
    - `RunOnceAsync` now returns the number of texts actually sent, not the number of readings found.
13. **`MeterSync/Application/AlertText.cs`: wrong date, and a crash on slim images.**
    - The date was formatted with `ar-SA`, whose calendar depends on the machine (Hijri on some), so a patient could read 1448 as the year.
    - `ar-SA` and `FindSystemTimeZoneById("Asia/Riyadh")` both throw without ICU or tzdata, so no alert could be built.
    - It now uses the invariant (Gregorian) culture and a fixed UTC+3 offset. Riyadh has no daylight saving.
14. **`Reading.cs`: alerts for old history.** The vendor returns every reading a meter ever uploaded, so linking a meter texted the patient about its whole history. Readings taken more than 24 hours before linking are now stored without an alert; the grace covers "measured, then linked". The first version of this check overflowed for an early link time; the tests caught it and it is fixed.
15. **`MeterSync/Application/SyncWorker.cs`: a timeout could stop the whole service.** An HTTP timeout is an `OperationCanceledException`, which the old catch let through, and .NET then stops the host. The catch now checks the stopping token instead. Sync and sweep each have their own try, so a failed sync no longer skips alerts already due.
16. **`MeterSync/Infrastructure/SyncDbContext.cs`: missing keys and indexes.**
    - Added the foreign keys: meter to patient, and reading to meter and to patient.
    - Added indexes on `readings.MeterId` and `readings.PatientId`, and the `AlertClaimedAt` column as `timestamptz`.
    - Ids now use `GENERATED ALWAYS AS IDENTITY`, per the house rule.

## Found, not fixed

- **No migration script.** The schema changed (new column, foreign keys, indexes, identity), and the house rule is reviewed up/down SQL scripts. These need generating before deploy. Existing orphan rows would block the new foreign keys.
- **Every pod polls the vendor for every meter every minute.** The work is safe to repeat now, but vendor load grows with the pod count. A leader lock or a single CronJob would cut it; that's the team's design call.
- **A crash between sending and recording an SMS** causes one repeat text after the 5-minute lease. Without a gateway idempotency key it's a choice between that and a lost alert. I chose the possible repeat.
- **A number that always fails** (an invalid phone, say) is retried every 5 minutes forever. There's no attempt limit or dead-letter state.
- **One SMS per out-of-range reading.** Grouping or throttling texts to the same patient is a product decision.
- **Stale uploads still alert.** A meter offline for a week that then uploads still triggers alerts for readings taken after linking. Whether those should text is the care team's call.
- **Phone numbers aren't validated or normalised** to `+9665...`, and `Patient` has no behaviour of its own.
- **No HSTS or HTTPS redirection.** TLS presumably ends at the ingress, and redirection would break in-process HTTP tests.
- **`/health/live` is a plain endpoint** with no database readiness check, and **`POST /meters` has no rate limiting**.

## Tests

The tests are in `/tmp/claude-0/syncruns/rules-only-2/MeterSync.Tests/` and need no database. They cover:
- HI/LO and mmol/L conversion;
- the alert thresholds and the 24-hour history grace;
- the SMS text: Gregorian date in Riyadh time, even with `ar-SA` as the current culture;
- the vendor URL keeping `/v2`;
- reading `sub`;
- the host in Production with the worker removed: health check 200, 401 without a token, CORS allow and deny, the audience check, both base addresses, and `PatientDirectory` not shared between scopes.

**Number of unit tests added: 25 test methods (49 cases).**