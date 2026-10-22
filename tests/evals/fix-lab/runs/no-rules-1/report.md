`HomeReadings` builds with 0 warnings and 0 errors. I added `HomeReadings.Tests/` with 12 unit tests covering the value parser and the alert threshold, and all 12 pass. Nothing that needs PostgreSQL ran here: not the endpoints, the notifier's SQL, the new foreign key, or the transaction around saving a reading and its alert.

## Fixed

**`Program.cs`**
- JWT was registered with `AddAuthentication()` and no default scheme. It now uses `JwtBearerDefaults.AuthenticationScheme`.
- Inbound claim mapping is off, so `sub` keeps its name and is read directly.
- `/health/live` also ran the database check, so a database outage would restart healthy instances. Live now runs no checks, and `/health/ready` runs the database check.
- The care team HTTP client used the standard resilience handler, which retries POSTs. Their system has no idempotency key, so a retry pages a nurse twice. POST retries are now disabled.
- Removed `AddMemoryCache`, since the cache is gone (see `ReadingQueries.cs`).

**`Api/CurrentPatient.cs`**
- It used `int.Parse` on a claim with a null-forgiving `!`. A missing or non-numeric `sub` now gives a 401 instead of a 500.

**`Api/LanguageMiddleware.cs`**
- It set `CurrentCulture` to `ar-SA` for Arabic users, which changed how numbers are parsed and formatted. Only the UI culture, which picks the message text, follows `x-language` now. The header match is case-insensitive.

**`Api/ErrorMiddleware.cs`**
- It returned the exception message and inner exception to the client. It now returns a generic message.
- Validation errors now return 400, and a client disconnect is not logged as a 500.
- It no longer writes to a response that has already started.

**`Application/ReadingService.cs`**
- `decimal.Parse(request.Value)` used the current culture, which is the bug the middleware change above would have exposed. Parsing is now invariant and accepts Arabic-Indic digits and `.`, `,` or `٫` as the decimal mark.
- Bad input (value, unit, national ID, notes, missing date) now returns 400 instead of a 500 from the database. The unit must be `mmol/L` or `mg/dL`.
- `collectedAt` was written to a `timestamptz` column with whatever kind it arrived as, which Npgsql rejects for non-UTC values. It is now converted to UTC. A time with no offset is treated as Riyadh local time.
- The reading and its alert were saved in two separate steps, so a crash between them lost the alert. They are now saved in one transaction.
- The log line destructured the whole reading, including national ID and glucose value. It now logs only the reading id and patient id.

**`Domain/Reading.cs`**
- The alert threshold of 11.1 is in mmol/L but was compared against the raw value. A normal 100 mg/dL reading would alert, and a dangerous one in mg/dL would be judged on the wrong scale. The comparison now converts mg/dL first. The care team now receives the value in mmol/L.

**`Application/ReadingQueries.cs`**
- `GET /readings/{id}` had no patient filter, so any patient could read another patient's reading. It is now scoped to the caller.
- `/readings/latest` cached under one global key, so patients saw each other's latest reading, and a new reading was invisible for up to a minute. I removed the cache.
- `/readings/today` used the UTC day. It now uses the Riyadh day (fixed UTC+3, no daylight saving).

**`Application/ReadingNotifier.cs`**
- An exception inside the loop stopped the whole host. It is now caught and logged.
- One failing alert blocked every alert behind it, forever. Each alert is handled separately now.
- Two instances could send the same alert. An alert is now claimed with a conditional update (`SentAt IS NULL`) before sending, and the claim is released if the send fails.
- It looked up each reading with a separate query. It now uses one join.

**`Infrastructure/PhotoStore.cs`**
- The path used the client's `file.FileName`, which allowed path traversal and overwrites. The file is now saved under a generated name, restricted to image extensions and 10 MB.
- A replaced photo is deleted, and only from under the photo root.

**`Infrastructure/ReadingsDbContext.cs`**
- Added a foreign key from `care_team_alerts.ReadingId` to `readings`.

## Found but not fixed
- **Alert delivery is not exactly once.** If the process crashes after an alert is claimed but before the send finishes, that alert is lost. If a send times out after the care team actually received it, the retry sends a duplicate. The care team's lack of an idempotency key makes both cases unavoidable on our side. A proper fix is a claim lease plus an attempt count, with a dead-letter state. I chose not to build that.
- **Photos live on local disk.** That is fine with a single volume, but with multiple instances they need shared storage.
- **No `Migrations` folder.** The schema is created some other way, and the new foreign key needs a migration or DDL wherever the production schema is managed.
- **National ID is only checked for length** (10 characters or fewer), not for Saudi ID format. I also don't check that it belongs to the signed-in patient, because I don't know the rule.
- **No rate limiting.** The `appsettings.json` password is a placeholder (`change-me`), and the Production connection string must come from secrets.
- **Unrelated photo and request-size limits.** Request-size limits are left at Kestrel defaults.