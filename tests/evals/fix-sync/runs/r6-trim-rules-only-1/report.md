# MeterSync review: what was fixed, and what wasn't

The service builds with 0 warnings. The NuGet audit (`NuGetAuditMode=all`, NU1901–NU1904 as errors) finds
nothing. The 31 unit tests in `MeterSync.Tests/` pass. Nothing that needs PostgreSQL could run here, so the
SQL behaviour (the claim UPDATE, the unique-violation handling, the new foreign keys) is reasoned through
but **not run**.

## Fixed

### Patient safety (readings and alerts)

1. **`MeterSyncService.cs`: HI and LO readings were thrown away.** `decimal.TryParse("HI")` fails, so the
   service logged a warning and skipped the reading. These are the most dangerous readings: over 600 mg/dL
   or under 20 mg/dL. They now become a High or Low alert, with `ValueMgDl` set to null
   (`Domain/GlucoseLevel.cs`, `Domain/Reading.cs`).
2. **`MeterSyncService.cs` / `Reading.cs`: mmol/L readings were compared to the mg/dL thresholds.** A normal
   5.6 mmol/L reading counted as "low", and a dangerous 15 mmol/L (270 mg/dL) one counted as "low" too. The
   value is now converted with ×18 and rounded to the column's 1 decimal place. A reading with an unknown
   unit is skipped and logged, never guessed at.
3. **`MeterSyncService.cs`: the parse used `NumberStyles.Number`, which allows thousands separators.**
   `"1,2"` was read as 12 mg/dL, a false low. It is now `AllowDecimalPoint` only. Values that can't come
   from a meter (≤ 0, over 1000 mg/dL, exponent notation) are rejected. They would also have overflowed
   `numeric(6,1)` and failed the whole save.
4. **`MeterSyncService.cs`: readings from before the meter was linked were stored and texted.** The vendor
   returns every reading the meter ever uploaded. Linking a used meter, or one with a long history, sent the
   patient an SMS for every old out-of-range reading, and those readings may belong to a previous owner.
   Readings taken before `LinkedAt` are now ignored.
5. **`AlertText.cs`: the date was formatted with `ar-SA`.** That culture's calendar is Hijri (Umm al-Qura)
   or Gregorian depending on the machine's ICU, and its digits can change too. A patient could get "1448"
   with no calendar named. The text now uses invariant formatting, a Gregorian date marked "م", and the time
   marked "بتوقيت الرياض". HI and LO are now worded "أعلى من 600" and "أقل من 20" instead of "HI mg/dL". The
   time zone is looked up once, not on every message.

### Duplicate and lost SMS (several pods)

6. **`AlertSweeper.cs`: every pod texted every due reading.** Each pod read the same 100 due rows and sent
   them all. The gateway has no idempotency key, so with two pods each patient got the alert twice. Each
   reading is now claimed with a conditional `UPDATE … WHERE alert_sent_at IS NULL AND (claimed_until IS
   NULL OR claimed_until < now)`. Only the pod whose UPDATE matched sends. The claim is a 5-minute lease
   (new column `AlertClaimedUntil`), so if a pod dies mid-send another one picks the reading up later.
7. **`AlertSweeper.cs`: one failed SMS stopped every alert.** An exception aborted the loop, and the failing
   reading (lowest Id) came first again on every run, so it blocked every alert behind it permanently. Each
   reading is now handled on its own:
   - If the gateway answered with an error (`SmsRejectedException`), the claim is released and the next run
     retries.
   - If there was no answer (timeout or network error), the claim is kept until the lease runs out, because
     the text may have gone out.
   - A patient with no phone is logged and skipped. Before, `phones[...]` threw `KeyNotFoundException`.
   - "Sent" is recorded with `CancellationToken.None`, so a shutdown just after sending doesn't cause a
     second text.
8. **`Program.cs` / `SmsClient.cs`: the SMS client had the 100-second default timeout.** It also used
   `EnsureSuccessStatusCode`, so a rejection couldn't be told apart from no answer. It now has a 20-second
   timeout and **deliberately no retry handler**, because a retried POST is a second SMS. `RunOnceAsync`
   now returns the number of texts actually sent.

### Sync robustness

9. **`MeterSyncService.cs`: two pods syncing at the same time collided on the unique `VendorId` index.**
   The second pod's whole `SaveChanges` threw, and the error repeated every minute. A unique violation now
   clears the tracker and returns 0. The other pod stored those rows, and anything it missed comes next run.
10. **`MeterSyncService.cs`: other ways one bad input stopped the whole sync.**
    - A vendor error for one meter (HTTP failure, timeout, bad JSON) stopped the sync for every meter. It is
      now caught per meter and logged.
    - A duplicate id inside one vendor response would have broken the insert. Responses are now de-duplicated.
    - Raw or unit values longer than their columns are now skipped.
    - Null `value` or `unit` fields no longer throw.
11. **`MeterSyncService.cs`: the "already known" query loaded every vendor id the meter ever had, every
    minute.** It now looks up only the ids in the response, across all meters, which matches the global
    unique index.
12. **`SyncWorker.cs`: a failed sync also skipped the alert sweep.** If the vendor or the sync was down,
    alerts already stored weren't sent. The two steps now have separate try/catch blocks.

### Security

13. **`MeterEndpoints.cs`: `POST /meters` linked a meter to whichever patient owned the national id in the
    body (IDOR).** Any signed-in patient could attach a meter to someone else, and the 404/201 answer told
    them which national ids were registered. The patient now comes from the token's `sub`, and the
    `nationalId` must match that patient, or the answer is 404. The request shape is unchanged.
14. **`Program.cs`: JWT `ValidateAudience = false`.** Any token the patients realm issued for any other app
    was accepted. The audience must now be `meter-sync`.
15. **`Program.cs`: CORS allowed every origin, with credentials.** Every website could call the API in the
    user's browser. Now only the `Cors:Origins` list is allowed, with GET/POST and no credentials (the API
    uses bearer tokens, not cookies). Config is read through options, so the host's configuration, tests
    included, applies.
16. **`MeterEndpoints.cs`: request validation and error answers.**
    - `nationalId` wasn't validated (null caused an exception). It is now required, at most 10 characters.
    - The serial number is trimmed before it's checked and stored.
    - Two requests racing past the `AnyAsync` check hit the unique index and got a 500. They now get
      409 Conflict.
17. **`CurrentPatient.cs`: a missing or non-numeric `sub` made `int.Parse` throw (500).** It is now
    `TryGetId`, and the endpoints answer 401.
18. **`Program.cs`: no exception handler, ProblemDetails or HSTS.** These are now added: `AddProblemDetails`,
    `UseExceptionHandler`, `UseStatusCodePages`, and `UseHsts` outside Development. Unhandled errors return an
    RFC 7807 body with no internals.

### Correctness and wiring

19. **`Program.cs`: the vendor URL lost its `/v2`.** The base address `https://meters.example.net/v2` has no
    trailing slash, so `meters/{sn}/readings` resolved to `https://meters.example.net/meters/...` and every
    vendor call went to the wrong path. The code now adds the slash
    (`MeterVendorClient.BaseAddressFrom`). The setting keeps its production value. A missing base address
    now fails with a clear message.
20. **`Program.cs`: `PatientDirectory` was a singleton holding the scoped `SyncDbContext`.** That's a captive
    dependency: one context shared by every request on every thread, which caused concurrency exceptions
    and stale data. It is now scoped. A test builds the container with `ValidateScopes`/`ValidateOnBuild`.
21. **`SyncDbContext.cs`: no foreign keys.** Readings and meters could point at patients and meters that
    don't exist. Foreign keys are now added (Restrict), plus indexes on `readings.MeterId` and `PatientId`.
22. **`MeterSync.csproj`: build and audit settings.** NuGet audit is now on (all, vulnerabilities as
    errors), and the nullable warnings CS8600/8602/8603 are now errors.

## Found, not fixed

- **No migration scripts.** The tests create the schema from the model. Production needs reviewed up and
  down SQL scripts for the schema changes: the new `AlertClaimedUntil` column, `ValueMgDl` becoming
  nullable, the foreign keys and the indexes. They weren't generated, because there's no database here and
  no existing migrations to follow.
- **The SMS gateway can still send twice in rare cases.** If a pod dies after the gateway accepted a
  message but before "sent" was recorded, or a send times out after the gateway actually sent it, the
  message goes out again after the lease runs out. That can't be fixed on our side without an idempotency
  key from the gateway. The trade-off chosen is "maybe twice" over "maybe never" for a glucose alert.
- **A number the gateway keeps rejecting is retried every minute, forever.** There's no attempt count or
  dead-letter state.
- **Stale alerts after linking.** A meter that uploads days late still triggers an SMS for old readings.
  That needs a clinical decision on a maximum alert age.
- **Every pod syncs every meter every minute.** With the duplicate handling above this is correct but
  doubles the vendor traffic. A PostgreSQL advisory lock or a leader would fix it.
- **The sync loads all meters and calls the vendor one meter at a time.** The vendor returns the full
  history each time and has no "since" parameter. This won't scale to many meters; it needs paging over
  meters and bounded parallelism.
- **Phone numbers aren't validated or normalised** (E.164, `+966…`). Patients are created outside this
  service, and the `Patient` constructor is fixed by the tests.
- **No HTTPS redirection.** TLS is assumed to end at the ingress, and redirecting would break plain-HTTP
  kubelet probes. HSTS is sent on HTTPS requests.
- **`GET /meters` has no paging.** It's bounded in practice: a patient has few meters.
- **Connection pool size:** `pods × Maximum Pool Size` must fit under PostgreSQL's `max_connections`. That's
  a deployment setting; the connection string isn't changed.
- **Readiness health check.** Only `/health/live` exists. A `/health/ready` that checks the database would
  help, but the routes were kept as they are.
- **Tests were written after the fixes, not first.** They pin the behaviour now, but they weren't seen
  failing against the old code. There are no Testcontainers integration tests, because Docker isn't
  available here.
