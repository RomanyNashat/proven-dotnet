I finished the review and fixed the service in place. `dotnet build HomeReadings/HomeReadings.csproj` succeeds with 0 warnings. I added 47 unit tests in `HomeReadings.Tests/` and they all pass. Nothing that needs PostgreSQL could run here, so these paths are untested: the alert claim SQL, the transaction and duplicate handling in `AddAsync`, and the patient filtering in the queries.

All the routes, request and response shapes, `ReadingsDbContext`, `ReadingNotifier.RunOnceAsync`, the typed `CareTeamClient`, `TimeProvider`, JWT on the default scheme, the setting names and Serilog-from-config are kept. Paths below are under `/tmp/claude-0/labruns/rules-only-1/HomeReadings/`.

## Fixed

**Patient data leaks**
1. **`Application/ReadingQueries.cs`, `GET /readings/{id}`**: it returned any patient's reading by id. Ids are sequential and easy to guess. It now filters by the signed-in patient and returns 404 otherwise.
2. **`ReadingQueries.LatestAsync`**: the cache key `readings:latest` was shared by all patients, so patient B could see patient A's newest reading. It also stayed stale for a minute after a new reading. I removed the cache, since this is a one-row indexed query, and removed `AddMemoryCache`.
3. **`Application/ReadingService.cs`, logging**: `{@Reading}` wrote the national ID, the value and the notes to the logs. It now logs only the reading id and whether it needs an alert.
4. **`Api/ErrorMiddleware.cs` (deleted)**: it sent the exception message and the inner message to the client, which can include Npgsql/EF details. `Program.cs` now uses `AddProblemDetails`, `UseExceptionHandler` and `UseStatusCodePages`, which return no exception text.

**Wrong numbers and dates**
5. **`Api/LanguageMiddleware.cs` (deleted), plus new `Api/AppLanguage.cs` and `Application/Messages.cs`**: an `ar` header set the formatting culture to ar-SA. ar-SA uses the Umm al-Qura (Hijri) calendar, and its decimal separator is "٫". I checked here that under ar-SA `decimal.Parse("7.8")` fails, so every Arabic user's POST failed with a 500. Building `new CultureInfo("ar-SA")` would also throw on an image without ICU. The header now only picks the message text, and no culture object is created.
6. **New `Application/MeterValue.cs`**: the meter's value was parsed with the current culture. Under en-US, "7,8" would quietly become 78. It now parses with the invariant culture, accepts a decimal point only, and also accepts Arabic-Indic digits and "٫".
7. **New `Application/Riyadh.cs`, used by `ReadingQueries.TodayAsync`**: "today" was the UTC day. Between midnight and 03:00 in Riyadh the home-screen chart showed the wrong day. It now uses the Riyadh day, with a fixed UTC+3 fallback when the image has no tzdata.
8. **`collectedAt`**: a time sent with an offset arrives with Kind Local, and one sent without an offset arrives with Kind Unspecified. Npgsql refuses both for a `timestamptz` column, so the save failed with a 500. Times are now converted to UTC, and a time with no offset is read as Riyadh time.
9. **`Domain/Reading.cs` and new `Domain/GlucoseUnit.cs`**: the alert rule compared the raw value with 11.1 whatever the unit. Every mg/dL reading (for example 120) would have paged a nurse. The rule now compares in mmol/L (200 mg/dL is the threshold).
   - Units are checked: mmol/L or mg/dL, any capitalisation, stored as "mmol/L" or "mg/dL".
   - The care team is sent the value in mmol/L.

**Validation and schema**
10. **New `Application/NewReadingValidator.cs`**: there was no input validation. A long national ID, unit or notes, or a value too large for the column, caused a database error and a 500. Bad input now gets a 400 with field errors. The rules are:
    - national ID is exactly 10 digits;
    - the unit is one of the two above;
    - the value is a number in a plausible range for its unit;
    - notes are at most 500 characters.
11. **`Infrastructure/ReadingsDbContext.cs`**, which changes the schema the tests create:
    - `Notes` had no maximum length, so it became an unbounded `text` column (banned by the column rules). It is now `varchar(500)`.
    - Alerts now have a foreign key to readings, and a unique index so a reading has at most one alert.
    - Readings have a unique index on (patient, collectedAt, value, unit), which replaces the old (patient, collectedAt) index.
    - Alerts have two new columns, `ClaimedUntil` and `Attempts`.

**Alerts to the care team**
12. **`ReadingService.AddAsync`**: the reading and its alert were saved separately, so a failure in between left a high reading with no alert. Both now commit in one transaction.
    - If the app sends the same reading again (a retry on a bad connection), the existing id comes back with 200 OK instead of a second reading and a second nurse alert. The first save still returns 201.
    - Two identical requests arriving at once are caught by the unique index and get the same answer.
13. **`Program.cs`, care team client**: `AddStandardResilienceHandler` retried the POST after timeouts and 5xx errors. With no idempotency key on the care team's side, each retry pages a nurse again. Retries are now off for POST (`DisableForUnsafeHttpMethods`); the notifier tries again on its next run instead.
14. **`Application/ReadingNotifier.cs` and `Domain/CareTeamAlert.cs`**:
    - Every pod sent every waiting alert, so nurses got duplicates. Alerts are now claimed first (`UPDATE … FOR UPDATE SKIP LOCKED … RETURNING`, batches of 10, a 10-minute claim). Two `RunOnceAsync` calls running at once never send the same alert.
    - Each alert's failure is handled on its own: the alert is released and the attempt counted, so one failure no longer blocks the rest of the batch.
    - It loads the readings in one query instead of one query per alert.
    - It records an alert as sent even if the request was cancelled after the nurse was paged.
    - It catches errors in its loop. Before, one care team or database error stopped the whole host.
    - In `Infrastructure/CareTeamClient.cs`, the response is now disposed.
15. **`Infrastructure/PhotoStore.cs` and the photo endpoint**: the client's file name went straight into the path, so `../../` could write files anywhere the process can.
    - The server now chooses the file name.
    - Only real JPEG or PNG files are accepted (checked from the file's first bytes), up to 5 MB, with a request size limit on the endpoint.
    - The file is cleaned up if the save fails, and the previous photo is deleted when a new one replaces it.

**Auth, health and settings (`Program.cs`, `Api/CurrentPatient.cs`)**
16. Authentication now names JWT bearer as the default scheme explicitly, clock skew is 30 seconds, and access is denied by default (the health endpoints stay anonymous).
    - Access needs a `sub` that is a positive int. Before, a non-numeric `sub` caused a 500 from `int.Parse`.
    - `sub` is read directly, with the mapped name-identifier claim as a fallback.
17. `/health/live` ran the database check, so a database outage would make Kubernetes restart every pod. Live now runs no checks; ready runs the database check.
18. The `CareTeam` and `Storage` settings are now validated at startup. The care team base address gets a trailing slash so the `api/` segment isn't dropped. Settings are read lazily from DI so test overrides apply. Serilog reads the `Serilog` section and adds the log context.

## Found but not fixed
- **`nationalId` comes from the request body.** A patient (or a buggy app) can send another person's ID, and the alert goes to that person's care team record. It should come from the identity token or a patient registry; that needs a decision on the claim name.
- **Meters show "HI" and "LO" outside their range.** These are now rejected with a 400, but "HI" is the most urgent reading there is. Storing and alerting on it needs a product/clinical decision and a change to the response shape.
- **There is no alert for low readings (hypoglycaemia),** which can be more urgent than high ones. This is a clinical rule decision.
- **A send that fails ambiguously** (a timeout after the care team already received it) is sent again on the next run, so a duplicate is still possible. I chose a possible duplicate over a missed alert. Only an idempotency key on their side removes this. I also send the value in mmol/L and did not add a `unit` field to their contract without knowing whether they accept it.
- **Future `collectedAt` is not rejected,** because meter clocks are often wrong and rejecting would lose the reading.
- **Encryption at rest is not handled in the app:** the national ID, notes and photos are stored unencrypted, so encryption is left to the database and the volume.
- **Migrations:** there is none for the schema changes; it has to be produced as a reviewed up/down script pair.
- **Still to decide:** database connection retries, HSTS (TLS is probably terminated at the ingress), and the PhotoRoot volume.
- **The connection string in `appsettings.json`** has a placeholder password, `change-me`. The real value has to be injected per environment.

## Tests added (`/tmp/claude-0/labruns/rules-only-1/HomeReadings.Tests/`)
- Value parsing under ar-SA, en-US and de-DE, plus Arabic digits.
- The Riyadh day boundary and UTC conversion.
- The alert rule in both units.
- The validator.
- Photo path traversal, file-type detection, and that delete never touches files outside the photo folder.
- No unbounded columns, plus the lease column and foreign key, both checked from the generated create script.
- The app run in Production with the database down: live returns 200, ready returns 503, and reading routes without a token return 401.
- The language header and the `sub` claim parsing.