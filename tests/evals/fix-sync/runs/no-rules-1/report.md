The service builds and my 10 unit tests pass. Nothing that needs PostgreSQL was run. That includes the `FOR UPDATE SKIP LOCKED` query in `AlertSweeper` and the unique-violation handling, which are untested, so run them against a real database before you ship.

## Fixed

**`Application/MeterSyncService.cs`**
- **`HI` and `LO` readings were dropped.** They failed the number parse and were skipped, so the most dangerous readings never produced an alert. `HI` now stores 601 mg/dL (High alert) and `LO` stores 19 mg/dL (Low alert).
- **mmol/L was never converted.** A 5.5 mmol/L reading was stored as 5.5 mg/dL and raised a false Low alert. It now converts at ×18. An unknown unit is skipped with a warning.
- **Parsing was too lenient.** `NumberStyles.Number` read `"1,5"` as 15. It now accepts a plain decimal point only.
- **Bad vendor data aborted the whole run.** Values or units too long for their columns, or out of range for `numeric(6,1)`, made `SaveChanges` fail for every meter. These readings are now skipped with a warning, and ids over 64 characters or duplicated in one response are handled.
- **Two pods syncing at once crashed on the unique `VendorId` index.** The service now saves per meter, catches the unique violation, re-reads and retries.
- **One failing meter stopped the rest.** A vendor HTTP error or timeout now only skips that meter.
- **The duplicate check was slow.** It loaded every known id into a `List` and called `Contains` on it for each reading. It now queries only the ids in the response and checks them in a `HashSet`.

**`Application/AlertSweeper.cs`**
- **Two pods could text the same patient.** Each reading is now claimed in a transaction with `FOR UPDATE SKIP LOCKED` and marked sent before commit. A crash between the SMS and the commit can resend an alert. I chose that over losing one.
- **One failure blocked every alert behind it.** A gateway error or missing phone number threw and aborted the batch. Each reading is now handled on its own and retried on the next run.

**`Application/SyncWorker.cs`**
- A failing sync no longer skips the alert sweep. They now run in separate try blocks.

**`Application/AlertText.cs`**
- **The SMS date was in the Hijri calendar.** `ar-SA` formats dates in Hijri, so patients saw a confusing date. It now uses the Gregorian calendar, still in Riyadh time.

**`Program.cs`**
- **The vendor base URL lost its `/v2`.** `https://meters.example.net/v2` has no trailing slash, so `meters/{serial}/readings` resolved to `/meters/...`. Both base addresses now get a trailing slash.
- **`PatientDirectory` was a singleton holding a scoped `DbContext`.** That shares one `DbContext` across requests and isn't thread-safe. It is now scoped.
- **Audience wasn't validated.** `ValidateAudience` was false, so any token from the issuer was accepted. It now requires `meter-sync`.
- **CORS allowed every origin with credentials.** It now allows only `Cors:Origins`.
- **The SMS client had no explicit timeout.** I set it to 15 seconds. I deliberately added no retries, because every POST sends a message.

**`Api/MeterEndpoints.cs` and `Api/CurrentPatient.cs`**
- **Any logged-in patient could link a meter to any national ID.** Linking now requires the national ID to match the caller's own `sub`. A mismatch returns 404, the same as an unknown patient.
- **A race on `POST /meters` returned a 500.** Two requests for the same serial could both pass the existence check. The unique violation on insert now returns 409.
- **A non-numeric `sub` claim returned a 500.** It now returns 401.

I added the tests in `MeterSync.Tests/`. They cover unit conversion, `HI`/`LO` alerts and the SMS date.

## Found, not fixed
- **Old readings trigger alerts.** The first sync of a newly linked meter pulls its full history, and every out-of-range reading in it gets an SMS, however old. I didn't pick a cut-off age because that is a clinical decision. I would add one.
- **No migrations or `EnsureCreated`.** Nothing in the service creates the schema, so production needs a migration step.
- **The connection string has no credentials.** `appsettings.json` has none, so they must come from the environment or a secret store.
- **A duplicate SMS is still possible.** If the gateway accepts a message but the response times out, the next run resends it. Without an idempotency key that can't be closed.
- **Readings are re-fetched in full every minute.** The vendor API returns every reading with no "since" parameter.
- **Only a liveness endpoint exists.** There is no readiness check against the database.
- **The SMS text uses the raw `HI`/`LO` strings.** An `HI` alert reads "قراءة السكر HI mg/dL". That is understandable but not polished Arabic.