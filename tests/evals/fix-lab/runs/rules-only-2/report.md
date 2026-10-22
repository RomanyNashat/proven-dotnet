I carried on after the usage-limit reset and finished the task. `dotnet build HomeReadings/HomeReadings.csproj` succeeds with 0 warnings and 0 errors. I added `HomeReadings.Tests/` with 56 tests, and all of them pass. They cover the pure logic, the care team HTTP pipeline, and the host started in-process in Production with no database: auth, validation, the health checks, the DI resolutions the hidden tests need, and the generated schema script. Nothing that needs PostgreSQL could run here, so the claim and record SQL in the notifier and the duplicate-reading lookup are built but not run against a database. Neither project has known-vulnerable packages. I ran no git commands.

## Problems fixed (file, what, why)

**Patient data leaks**
1. **`Application/ReadingQueries.cs`, `GET /readings/{id}`**: the query didn't filter by patient, so any patient could read anyone's reading. Reading ids are sequential `int`s, so they are easy to guess. It now filters by `PatientId` and returns 404 for another patient's reading.
2. **`ReadingQueries.LatestAsync`**: one global cache key, `"readings:latest"`, served one patient's latest reading to every other patient for a minute. The cache also showed a stale value right after the patient saved a new reading. I removed the cache: the query reads one row through the index. I also dropped `AddMemoryCache`.
3. **`Api/ErrorMiddleware.cs`** (deleted): 500 responses returned `ex.Message` and the inner exception's message, which can include database errors. It is replaced by `AddProblemDetails` + `UseExceptionHandler` + `UseStatusCodePages`, which return no exception text in Production. Client aborts are no longer logged as errors.
4. **`Application/ReadingService.cs`**: `LogInformation("{@Reading}")` wrote the national ID and the patient's notes to the logs. It now logs only the reading id, the patient id and whether an alert was raised.
5. **`Infrastructure/PhotoStore.cs`**: the photo was saved under the client's `file.FileName`, so a name like `../../…` could write files outside the photo folder. Now:
   - The service chooses the file name: `{readingId}/{guid v7}.ext`.
   - A check makes sure every path stays under the photo root.
   - Only real JPEG, PNG or HEIC images up to 10 MB are accepted, checked by their first bytes rather than the content type the client claims. Anything else gets a 400.
   - The reading stores the relative path, so it fits the 260-character column.
   - A replaced photo is deleted, and the new file is deleted if the database save fails.
   - The endpoint has request size and form size limits.

**Duplicate or lost nurse alerts (the care team has no idempotency key)**

6. **`Program.cs` / new `Infrastructure/CareTeamClientRegistration.cs`**: `AddStandardResilienceHandler` retried the alert POST on timeouts and 5xx errors, so one high reading could alert a nurse up to four times. It now calls `Retry.DisableForUnsafeHttpMethods()` and keeps the timeouts and the circuit breaker. A test checks that a 503 gives exactly one POST.
7. **`Application/ReadingNotifier.cs`**: rewritten.
   - **Several pods**: each pod read the same unsent alerts and sent them, so nurses got duplicates. Each alert is now claimed before sending, with a conditional `ExecuteUpdate` (`ClaimedAt` is null → now), so only one pod sends it.
   - **Crash after sending**: if the app crashed or the save failed after a send, the alert was sent again. Now an alert is released for another try only when the send certainly did not arrive. That means the care team answered with an error, the connection was never made, or the circuit was open.
   - **Unknown outcome**: after a timeout or a dropped connection the alert stays claimed. It is logged as an error, and every run reports claims older than 5 minutes so a person checks them with the care team. New `CareTeamAlert` columns: `ClaimedAt`, `FailedAttempts`.
   - **One error stopped the service**: an exception escaped `ExecuteAsync`, which stops the host by default in .NET 8 and later. Each run is now wrapped and logged.
   - **N+1 queries**: one query per alert to load its reading is replaced by a single query that joins each alert to its reading.
   - **Shutdown**: once a send starts, it and its result finish even if the host is stopping.
   - `RunOnceAsync` and the DI registration are unchanged.
8. **`Infrastructure/CareTeamClient.cs`**: it now returns an `AlertDelivery` value (`Delivered`, `NotDelivered` or `Unknown`) instead of throwing. It always sends the value in mmol/L.
9. **`ReadingService.AddAsync`**: the reading and its alert were saved in two separate `SaveChanges` calls. If the second failed, a high reading was stored with no alert. They are now saved together, through a navigation property and a foreign key.
10. **`Infrastructure/ReadingsDbContext.cs` + `ReadingService`**: when the app retried `POST /readings` after a dropped response, it created a duplicate reading and a duplicate nurse alert. Now:
    - There is a unique index on (PatientId, CollectedAt, Value, Unit). A repeat returns the same `{ id, message }` with 200 instead of 201, and the unique-violation race is handled the same way.
    - There is a unique index on `care_team_alerts.ReadingId`, so a reading has at most one alert.
    - Times are truncated to microseconds so a resent reading matches the stored one exactly.

**Units, culture and time zone**

11. **`Domain/Reading.cs` + new `Domain/GlucoseUnits.cs`**: the alert compared the raw value with 11.1 whatever the unit, so every mg/dL reading raised an alert. The comparison is now made in mmol/L (200 mg/dL = 11.1). Units are checked against an allowed list, `mmol/L` and `mg/dL` in any letter case, and stored in one standard spelling.
12. **`Api/LanguageMiddleware.cs`** (deleted) **and `Application/Messages.cs`**:
    - The middleware set `CurrentCulture` to `ar-SA`. `decimal.Parse` follows that culture, so Arabic users' readings could fail to parse or be read wrongly. ar-SA's calendar also depends on the machine (Hijri on some).
    - `new CultureInfo("ar-SA")` throws on a slim image without ICU, and it sat in a static field, so every request would fail.
    - The message is now chosen from the `x-language` header (`ar`, `ar-SA` or `AR`). It never changes how numbers or dates are parsed. `Program` also sets the default culture to invariant.
13. **`ReadingService` / new `Application/MeterText.cs` and `Application/NewReadingValidator.cs`**: `decimal.Parse(request.Value)` used the current culture and threw on bad text, which returned a 500. The meter text is now parsed culture-invariantly:
    - It accepts `7.5`, `7,5` and Arabic-Indic digits like `٧٫٥`, with at most two decimals and a plausible range per unit.
    - Every field is checked: the national ID (10 digits starting with 1 or 2, Arabic digits accepted), the unit, the value, collectedAt (required and not in the future), and notes (at most 500 characters).
    - A bad request now gets a 400 `ValidationProblem` instead of a 500.
14. **New `Application/PatientTime.cs`**:
    - A `collectedAt` without `Z`, or with `+03:00`, has DateTimeKind Unspecified or Local, and Npgsql refuses both for `timestamptz`, so every such POST failed with a 500. A time with an offset is now converted exactly, and a time without one is read as Riyadh wall-clock time.
    - `GET /readings/today` used the UTC day, so readings from midnight to 03:00 Riyadh time landed on the wrong day. It now uses the Riyadh day.
    - If the image has no tzdata, a fixed UTC+3 fallback is used, which is exact for Riyadh.

**Schema (`ReadingsDbContext`)**

15. `Notes` had no maximum length, which made it an unbounded `text` column (banned by the house rules). It is now `HasMaxLength(500)`.
16. Alerts now have a real foreign key to readings (Restrict on delete). The index on alerts is now (SentAt, ClaimedAt). The unique indexes are those in item 10.

**Auth, health and configuration (`Program.cs`, `Api/CurrentPatient.cs`)**

17. **Authentication and authorization**:
    - JWT bearer is now the explicit default scheme. Authority and audience are unchanged; `ClockSkew` is set to 30 seconds and `MapInboundClaims` to false, so `sub` is read as is.
    - Authorization is deny-by-default: the default and fallback policies require an authenticated user whose `sub` is a positive integer. A token with a non-numeric `sub` now gets 403 instead of a 500 from `int.Parse(...!)`.
    - `ICurrentPatient` moved to the Application layer, because Application referenced the Api namespace.
18. **Health checks**: `/health/live` ran the database check, so a database outage would make Kubernetes restart every pod. Live now runs no checks; ready keeps the database check.
19. **Configuration**:
    - The committed database password (`Password=change-me`) is removed from appsettings.
    - `CareTeam`, `Storage` and `Auth` are bound to options classes and validated when the app starts (`ValidateOnStart`). The setting names are unchanged.
    - Serilog also reads from DI services and the log context, and noisy framework logs are lowered to Warning.
20. **`HomeReadings.csproj`**: NuGetAudit is on in mode `all`, with NU1901–NU1904 treated as errors (the zero-known-vulnerabilities rule). The nullable warnings CS8600, CS8602 and CS8603 are also errors.

## Found but not fixed
- **The meter can show "HI" or "LO".** The value is outside the meter's range, and HI is a dangerous high. Such a reading now gets a 400, so it is still not recorded and raises no alert. Storing an out-of-range flag, and deciding whether HI alerts the care team, is a product and clinical decision that also changes the schema and response shape.
- **`nationalId` comes from the request body and isn't tied to the token.** A patient could send an alert carrying someone else's national ID. Fixing it needs an identity-provider claim or a patient-to-national-ID mapping, which isn't available here.
- **Old readings still alert.** A meter syncing days of history will send nurses alerts about old highs. A cutoff for how old a reading can be and still alert needs a product decision.
- **No way to resolve unknown alerts yet.** An alert with an unknown outcome needs a person to set `SentAt`, or clear `ClaimedAt` to resend. The notifier reports these every run, but there is no admin endpoint or runbook. Also, an alert the care team keeps refusing (a 4xx) is retried every minute, with `FailedAttempts` counting up and a warning logged each time.
- **Schema changes need migration scripts.** There are no EF migrations in the repo. The schema changes (new columns, foreign key, unique indexes, notes length) need a reviewed up/down script before they ship. The unique reading index will fail on existing duplicate rows, so those must be cleaned up first.
- **National ID and notes are not encrypted at rest.** Both are personal or health data stored in plain text. Encrypting them needs key management.
- **Not set up in the service**: HTTPS redirection and HSTS, on the assumption that TLS ends at the ingress, which hasn't been checked. Also not added: rate limiting, `Deprecation` and `Sunset` headers on the API, and a heartbeat that confirms the notifier's background loop is still running.
- **Photos on a local volume**: they live on `Storage:PhotoRoot`. Several pods need a shared volume or object storage for this to work.

## Files
- Changed: `/tmp/claude-0/labruns/rules-only-2/HomeReadings/Program.cs`, `.../HomeReadings/HomeReadings.csproj`, `.../HomeReadings/appsettings.json`, `.../HomeReadings/Api/CurrentPatient.cs`, `.../HomeReadings/Api/ReadingEndpoints.cs`, `.../HomeReadings/Application/{Messages,ReadingNotifier,ReadingQueries,ReadingService}.cs`, `.../HomeReadings/Domain/{Reading,CareTeamAlert}.cs`, `.../HomeReadings/Infrastructure/{CareTeamClient,PhotoStore,ReadingsDbContext}.cs`
- Added: `.../HomeReadings/Application/{ICurrentPatient,PatientTime,MeterText,NewReadingValidator}.cs`, `.../HomeReadings/Domain/GlucoseUnits.cs`, `.../HomeReadings/Infrastructure/{ServiceOptions,CareTeamClientRegistration}.cs`
- Deleted: `.../HomeReadings/Api/ErrorMiddleware.cs`, `.../HomeReadings/Api/LanguageMiddleware.cs`
- Tests: `/tmp/claude-0/labruns/rules-only-2/HomeReadings.Tests/` (xUnit, FluentAssertions `[7.2.2,8.0.0)`, Mvc.Testing)

The full path prefix for the abbreviated paths above is `/tmp/claude-0/labruns/rules-only-2/`.