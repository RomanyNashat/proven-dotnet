I fixed 24 problems in MeterSync; the list is below. I couldn't save it to `report.md`: the environment blocked writing that file and said to return it as text, so the folder has no `report.md`. The service builds with no warnings. I added 31 unit tests (57 cases counting theory rows) in `MeterSync.Tests/`, and they all pass. No vulnerable packages, direct or transitive.

## Fixed (file, what, why)

1. **`Program.cs`, new `Infrastructure/HttpBaseAddress.cs`**: the vendor base address gets a trailing slash, and both addresses must be absolute http(s), checked at start-up. Without the slash, `https://meters.example.net/v2` plus `meters/x/readings` became `https://meters.example.net/meters/x/readings`, so every sync called the wrong URL.
2. **`Application/MeterSyncService.cs`, new `Application/GlucoseValue.cs`**: `HI` and `LO` are now stored at the meter's limits (600 and 20 mg/dL), so they raise High and Low alerts. Before, they were skipped as "not a number", and they are the most dangerous readings.
3. **Same files**: `mmol/L` values are converted to mg/dL (×18), and unknown units are rejected. Before, 5.5 mmol/L (a normal 99 mg/dL) and 15 mmol/L (270, high) both sent a "low" text.
4. **Same files**: stricter parsing (`AllowDecimalPoint` only), values outside 0–1000 mg/dL rejected, length and missing-field checks, and `VendorReading` fields made nullable. The old parse accepted `1,120` and negatives, and a value too long for its column failed the whole save.
5. **`Application/MeterSyncService.cs`**:
   - Each meter is saved and error-handled on its own.
   - A duplicate insert by another pod (PostgreSQL unique violation) clears the tracker, and the run carries on.
   - New readings are checked against all stored vendor ids, not just that meter's.
   - Before, one vendor error or one duplicate from a second pod threw away every meter's new readings, every minute.
6. **`Application/MeterSyncService.cs`**: the reading value is no longer logged. It is health data.
7. **`Application/AlertSweeper.cs`, `Domain/Reading.cs`, `Infrastructure/SyncDbContext.cs`**:
   - Each alert is claimed before sending with a conditional UPDATE on a new `AlertClaimedUntil` column (a 5-minute lease). Only the pod that wins the claim sends the text.
   - `AlertSentAt` is written even during shutdown, and a failed send releases the claim.
   - Before, every pod swept the same rows. The gateway has no idempotency key, so patients got the same text twice. The lease means a pod that dies mid-send doesn't lose the alert.
8. **`Application/AlertSweeper.cs`**: one failed SMS or a missing phone number no longer stops the batch. Only readings from the last 24 hours are texted. The vendor returns a meter's whole history, so the first sync after linking used to text about readings from weeks ago.
9. **`AlertSweeper.cs`, `Program.cs`**: `AlertSweeper` is now scoped and takes `SyncDbContext` directly, like `MeterSyncService`. `RunOnceAsync` returns how many texts it actually sent.
10. **`Program.cs`**: `PatientDirectory` is now scoped. As a singleton it held one `SyncDbContext` that all concurrent `GET /meters` requests shared for the life of the pod, and DbContext isn't thread-safe.
11. **`Application/AlertText.cs`**: dates use the invariant culture, Gregorian `dd/MM/yyyy` with "م", plus "بتوقيت الرياض". `ar-SA` can give a Hijri date that looks Gregorian, and on a slim image without ICU it can't be created at all, so every alert failed.
12. **`Application/AlertText.cs`**: a fixed UTC+3 offset replaces `FindSystemTimeZoneById("Asia/Riyadh")`, which throws on an image without tzdata. Riyadh has no daylight saving, so this is exact.
13. **`Application/AlertText.cs`**: `HI` and `LO` get their own wording ("above/below the meter's range", "very high/low") instead of "HI mg/dL".
14. **`Api/MeterEndpoints.cs`**: `POST /meters` only links a meter to the caller's own record (the national id must belong to the patient in `sub`). Any other national id gets 404, the same as an unknown one. Before, any signed-in patient could attach a meter to anyone's national id, sending that person's readings and texts into someone else's record, and could probe which national ids exist.
15. **`Api/MeterEndpoints.cs`**: a duplicate serial number caught at save time returns 409. Two concurrent links of the same serial used to give a 500.
16. **`Api/MeterEndpoints.cs`**: `NationalId` is validated (required, at most 10 characters).
17. **`Api/CurrentPatient.cs`**: `int.Parse` is replaced by `TryGetId`. A token without a numeric `sub` gets 403 instead of a 500.
18. **`Program.cs`**: JWT bearer now validates the audience `meter-sync`. Before, a token the same realm issued for any other client was accepted.
19. **`Program.cs`, new `Api/CorsOrigins.cs`**: CORS allows only the `Cors:Origins` list (read as an array or a comma-separated value), GET and POST, no credentials. Before, any origin with credentials was allowed, and the setting was never read.
20. **`Program.cs`**: the SMS client has a 15 s timeout and deliberately no retry handler. A retried POST would send the text twice.
21. **`Application/SyncWorker.cs`**: sync and sweep run as separate steps, each in its own scope, and only shutdown cancellation stops the loop. An HttpClient timeout is also a cancellation exception: it used to escape the loop, stop the worker and stop the host. A failed sync also skipped the sweep.
22. **`Program.cs`**: added ProblemDetails with the exception handler, HSTS outside Development, and `nosniff` / `X-Frame-Options` headers.
23. **`Infrastructure/SyncDbContext.cs`**: added foreign keys Meter→Patient, Reading→Meter and Reading→Patient (restrict on delete), and `GENERATED ALWAYS AS IDENTITY` keys (house rule).
24. **New `Infrastructure/DbErrors.cs`**: a helper that recognises a PostgreSQL unique violation (`23505`), used by fixes 5 and 15.

## Found, not fixed

- **No migration script.** The new `AlertClaimedUntil timestamptz` column, the foreign keys and the identity change need reviewed up and down scripts for any existing database.
- **Readings from before linking are still imported.** The meter might be second-hand. Whether that history belongs to the new patient is a product and privacy decision.
- **The 24 h alert window and 5-minute lease are my choices.** The care team should confirm them. A longer SMS outage now drops alerts older than 24 h.
- **A duplicate text is still possible** when the gateway accepts a message but the response is lost, or the pod dies before writing `AlertSentAt`. Without an idempotency key the choice is at-least-once or at-most-once. I chose at-least-once, because a missed low-glucose alert is worse.
- **HI/LO advice wording.** The advice line is the same for every alert. HI and LO probably need urgent wording, which is the care team's call.
- **Phone numbers** aren't validated or normalised to the `+9665…` format the gateway uses.
- **Every pod calls the vendor for every meter every minute**, and `SyncOnceAsync` loads all meters at once. The data stays correct, but this needs a leader lock or paging at scale.
- **Layering:** `PatientDirectory` (Infrastructure) returns the API's `MeterResponse`.
- **No HTTPS redirect** (I assumed TLS ends at the ingress), and **no readiness check** for the database.
- **The database paths aren't tested**: claims, duplicate handling, and the race between pods. They need Testcontainers integration tests, and there is no PostgreSQL here.

## Unit tests added

31 test methods (57 cases): glucose parsing and conversion, alert thresholds, SMS text (date, digits, `ar-SA` culture, HI/LO), base addresses and CORS origins, token subject and request validation, and a host started in Production with scope validation (health, 401 without a token, CORS preflight, the `v2` vendor address, the JWT audience).

Files are in /tmp/claude-0/syncruns/rules-only-1:
- MeterSync/Program.cs
- MeterSync/Api/MeterEndpoints.cs
- MeterSync/Api/CurrentPatient.cs
- MeterSync/Api/CorsOrigins.cs
- MeterSync/Application/MeterSyncService.cs
- MeterSync/Application/AlertSweeper.cs
- MeterSync/Application/AlertText.cs
- MeterSync/Application/GlucoseValue.cs
- MeterSync/Application/SyncWorker.cs
- MeterSync/Domain/Reading.cs
- MeterSync/Infrastructure/SyncDbContext.cs
- MeterSync/Infrastructure/MeterVendorClient.cs
- MeterSync/Infrastructure/HttpBaseAddress.cs
- MeterSync/Infrastructure/DbErrors.cs
- MeterSync.Tests/ (5 test files and MeterSync.Tests.csproj, FluentAssertions pinned to `[7.2.2,8.0.0)`)