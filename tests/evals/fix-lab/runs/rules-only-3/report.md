I fixed 17 problems in `HomeReadings/`. `dotnet build HomeReadings/HomeReadings.csproj` succeeds with 0 warnings and no vulnerable packages, and the 44 tests I added in `HomeReadings.Tests/` all pass. Nothing that needs PostgreSQL could run here, so the two database-dependent fixes (alert claiming in `ReadingNotifier`, and recognising a resent reading) are built but not tested.

The protected contract still holds: `Program`, every route with its request and response shapes, `ReadingsDbContext`, `ReadingNotifier.RunOnceAsync`, the typed `CareTeamClient`, `TimeProvider` from DI, JWT bearer on the default scheme (authority `Auth:Authority`, audience `home-readings`), the three settings, and Serilog from its config section.

## Problems fixed (file, what, why)

1. **`Application/ReadingQueries.cs`, `GET /readings/{id}`**: the lookup didn't check who owned the reading, so any patient could read anyone's reading by guessing the next id. It now also filters by the patient's id, and another patient's reading returns 404.
2. **`ReadingQueries.cs`, `GET /readings/latest`**: the cache key was the same for everyone ("readings:latest"), so one patient's latest reading was shown to every patient for a minute. It also went stale right after a new reading. I removed the cache: the lookup is a single index seek.
3. **`ReadingQueries.cs` and new `Application/RiyadhTime.cs`, `GET /readings/today`**: "today" meant the UTC day, but patients are in Riyadh (UTC+3), so readings between midnight and 3 a.m. landed on the wrong day. It now uses the Riyadh day. I used a fixed +3 offset: Saudi Arabia has no daylight saving, and this way it doesn't need tzdata, which Alpine images lack.
4. **`collectedAt` handling (`RiyadhTime.ToUtc`, used by the validator)**: a time sent with an offset or without "Z" made Npgsql throw on the `timestamptz` column, so the request failed with a 500. Every time is now converted to UTC; a time with no offset is treated as Riyadh time.
5. **`Api/LanguageMiddleware.cs` (deleted), `Application/Messages.cs`, new `Application/MeterText.cs`**:
   - **The culture problem:** `x-language: ar` switched the request's number and date culture to ar-SA. The meter value was then parsed with `decimal.Parse` under that culture, which depends on the machine's ICU data (Arabic separators, Hijri calendar).
   - **Language:** it now comes straight from the header, and only the message text changes.
   - **Meter value:** parsed with the invariant culture, accepting "7.5", "7,5" and Arabic-Indic digits ("٧٫٥").
6. **New `Application/NewReadingValidator.cs`**: nothing was validated before. A bad value gave a 500, any unit was accepted, and so were any national ID, a future `collectedAt`, and notes of any length. Each now gets a 400 that names the field.
7. **`Domain/Reading.cs`, units**: the alert compared every value against 11.1 whatever its unit, so a normal mg/dL reading of 95 alerted a nurse. The unit must now be mmol/L or mg/dL, and the threshold matches the unit (200 for mg/dL). The care team is sent the value in mmol/L.
8. **`ReadingService.cs` and `ReadingsDbContext.cs`, saving a reading**:
   - **Two saves:** the reading and its alert were saved separately, so a high reading could be stored without its alert. They are now saved together in one transaction.
   - **App retries:** the mobile app's retries created duplicate readings, and so duplicate nurse alerts. A unique index on (patient, collectedAt, value) catches them. A repeat, including two arriving at once, returns the stored reading with the same `{ id, message }`.
9. **`Program.cs`, `Application/ReadingNotifier.cs`, `Domain/CareTeamAlert.cs`, `Infrastructure/CareTeamClient.cs`, duplicate nurse alerts**:
   - **POST retries:** the standard resilience handler retried the POST to the care team, whose system has no idempotency key, so one failure could become up to four nurse alerts. Retries for POST are now off.
   - **Two pods:** both could send the same alert. Each alert is now claimed in the database (new `ClaimedAt` column) before it is sent.
   - **When a send fails:** if the care team clearly didn't take it (an error status, connection refused, circuit open), the claim is released and the next run tries again. If the outcome is unknown (a timeout), it waits 15 minutes before sending again.
   - **Per-alert updates:** state now changes in single atomic database updates. "Sent" is recorded even during shutdown.
10. **`ReadingNotifier.cs`, background loop**: one care-team or database error stopped the whole host. Errors are now logged and the loop carries on, and one failed alert no longer stops the rest of the batch.
11. **`Infrastructure/PhotoStore.cs`, `ReadingService.cs` and `ReadingEndpoints.cs`, photo upload**:
    - **Path traversal:** the photo was saved under the file name the client sent, so a name with `../` or an absolute path could write anywhere on disk. The service now picks the name (`{id}/photo.jpg` or `.png`) and stores the path relative to `Storage:PhotoRoot`.
    - **Limits:** any size or content was accepted. Uploads are now capped at 5 MB, and only real JPEG or PNG (checked by the file's first bytes) is accepted.
    - **Half-written files:** a failed upload could leave a broken file. It is now written to a temporary file and moved into place.
12. **`Api/ErrorMiddleware.cs` (deleted), `Program.cs`, error responses**: errors returned the exception message and inner message (SQL and connection details) to the client. They now use the built-in exception handler with ProblemDetails, with no exception text. They keep an `error` field the app can show, in Arabic or English.
13. **`ReadingService.cs`, logging**: `{@Reading}` wrote the national ID, value and notes to the logs, which is patient data. Only the reading id, patient id and whether it alerted are logged now.
14. **`Api/CurrentPatient.cs` and `Program.cs`, authorization**:
    - **Bad `sub`:** a token whose `sub` wasn't a number caused a 500. A missing or non-numeric `sub` now gets 403.
    - **Deny by default:** that patient check is now the default and fallback authorization policy.
    - **JWT setup:** the default scheme is now set explicitly and clock skew is 30 seconds.
15. **`Program.cs`, health checks**: `/health/live` checked the database, so a database outage would make Kubernetes restart every pod. Live now has no checks; ready checks the database.
16. **`Program.cs`, configuration**:
    - **Read too early:** the connection string, care-team address, JWT authority and Serilog settings were read before the app was built, which can miss configuration added later (the host, or the tests' `WebApplicationFactory`). They are now read when services are built.
    - **Fail at start:** `Storage:PhotoRoot` and `CareTeam:BaseAddress` are checked at startup.
    - **Base address:** a trailing slash is added if missing, so the URL to "alerts" is built correctly.
17. **Other fixes**:
    - **`appsettings.json`:** removed the committed database password (it now comes from the environment) and quietened framework log noise.
    - **`ReadingsDbContext.cs`:** `Notes` had no length, so it was an unbounded `text` column; it is now `varchar(500)`. Added a foreign key from alert to reading and an index for waiting alerts.
    - **`HomeReadings.csproj`:** a known-vulnerable package now fails the build (NuGet audit), as do the main nullable warnings.

Tests are in `/tmp/claude-0/labruns/rules-only-3/HomeReadings.Tests/`:
- **Parsing, Riyadh time, units and validation:** meter-text parsing, including under an ar-SA culture; Riyadh day boundaries; thresholds per unit; the validator; reading the patient id from `sub`.
- **Photo store:** a `../` file name stays under the root; type and size checks.
- **Database model:** checks the create script.
- **In-process without a database:**
  - live health is 200;
  - no token gets 401 and a non-numeric `sub` gets 403;
  - a bad value gets a 400 that names it;
  - a database failure returns a ProblemDetails body with no exception text.

## Found but not fixed
- **Meter "HI" and "LO" are rejected with a 400.** Storing them needs a decision on how to represent them. There is also no alert for low readings, which are dangerous. Both are clinical and product decisions.
- **`nationalId` is taken from the request body**, not from the token. A patient can send any valid ID, and the care team is alerted with it. It is also stored unencrypted on every reading. This needs a patient record or an ID claim in the token, plus encryption at rest.
- **The schema changed** (new `ClaimedAt` column, unique indexes, foreign key, `Notes` length). There are no migrations in the repo, so it needs a reviewed up/down script. Existing duplicate readings would block the unique index.
- **Duplicate-alert gaps remain.** If the care team actually got an alert but we didn't get the answer, or recording "sent" fails after a successful send, a duplicate goes out after 15 minutes. A 4xx from the care team is retried every minute with no limit. I chose a duplicate over a missed high reading.
- **Two genuine readings with the same time and same value** are treated as one.
- **The 500 response body changed** from `{ error, detail }` to ProblemDetails plus `error`. The app should still find `error`.
- **Photos are on a local volume.** Several pods need shared storage or object storage. Switching from .jpg to .png leaves the old file behind.
- **Not added:** HTTPS redirection or HSTS (TLS is assumed to end at the ingress), forwarded headers, rate limiting, and audit logging of who read patient data.