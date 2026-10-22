The service builds cleanly. I couldn't run it, because there's no PostgreSQL here. I also didn't write any unit tests, so none of these changes have been run. The notifier's `FOR UPDATE SKIP LOCKED` query is the part I'm least sure of, because it's raw SQL against EF's default column names.

## Fixed

**Data exposure**
- **`Application/ReadingQueries.cs`:**
  - `GET /readings/{id}` returned any patient's reading. It now filters by the signed-in patient and returns 404 otherwise.
  - `GET /readings/latest` used one cache key for all patients, so it showed patient A's reading to patient B, and could be a minute stale. I removed the cache.
- **`Application/ReadingService.cs`:** the log line wrote the whole reading, including the national ID and health data. It now logs only the reading and patient ids.
- **`Api/ErrorMiddleware.cs`:** it returned exception messages and inner exceptions to the client. It now returns a generic message with a 500. `RequestRejectedException` becomes a 400 and `UnauthorizedAccessException` a 401. Cancelled requests are no longer logged as errors.

**Care-team alerts** (no idempotency key, so a duplicate pages a nurse twice)
- **`Program.cs`:** the standard resilience handler was retrying the alert POSTs. I turned retries off for POST. A failed send stays unsent and goes out on the next run.
- **`Application/ReadingNotifier.cs`:**
  - An exception in the background loop would have stopped the whole host. It is now caught and logged.
  - One failing alert aborted the whole batch. Each alert now has its own scope, and the others still go out.
  - Two instances could send the same alert. Each alert is now claimed in a transaction with `FOR UPDATE SKIP LOCKED`, so only one instance sends it.
- **`Application/ReadingService.cs`:** the reading and its alert were saved separately, so a crash between the two lost the alert for a high reading. They are now saved in one transaction.
- **`Domain/Reading.cs`:** `NeedsAlert` compared the raw number to 11.1 mmol/L whatever the unit. A mg/dL reading of 150 would have alerted, and a mg/dL reading of 250 would have missed the 200 threshold. mg/dL is now converted before the check.

**Meter values and the Riyadh timezone**
- **`Application/ReadingService.cs`:**
  - `decimal.Parse(request.Value)` used the request's culture. With `x-language: ar` it could misread or throw on the meter's text. It now parses invariantly, accepts Arabic-Indic digits and `,` or `٫` as the decimal mark, and rejects values that are zero, negative or too large. A bad value returns 400 instead of 500.
  - It now validates the unit (`mmol/L` or `mg/dL`, case-insensitive), the 10-digit national ID and the notes length.
  - A `collectedAt` with no offset made Npgsql throw on the `timestamptz` column. It is now treated as Riyadh time and stored as UTC.
- **`Api/LanguageMiddleware.cs`:** it set `CurrentCulture` to `ar-SA`, which changes number and calendar formatting. Now only the UI culture follows `x-language`, and the header is matched leniently.
- **`Application/ReadingQueries.cs`:** "today" used the UTC date. It now uses Riyadh's day (UTC+3, no daylight saving).

**Photos**
- **`Infrastructure/PhotoStore.cs`:**
  - It used the client's file name, so a crafted name could write outside the photo root or overwrite another file. It now generates a name.
  - Empty files, files over 10 MB and anything other than JPEG or PNG now return 400.
  - Partial files are cleaned up on failure.
  - The database now stores the path relative to `Storage:PhotoRoot`, not an absolute one.
- **`Application/ReadingService.cs`:** replacing a photo now deletes the old file.

**Other**
- **`Program.cs`:**
  - `/health/live` also checked the database, so a database outage would have restarted every instance. Live now checks nothing. Ready checks the database.
  - JWT is now the explicit default scheme.
  - The `CareTeam:BaseAddress` always gets a trailing slash, so the `alerts` path resolves correctly.
- **`Api/CurrentPatient.cs`:** it threw a null or format exception (a 500) on a missing or odd `sub`. It now reads `sub`, falls back to the name-identifier claim, and returns 401 if neither is valid.
- **`Infrastructure/ReadingsDbContext.cs`:** I added a foreign key from `care_team_alerts.ReadingId` to `readings`.
- **`appsettings.json`:** ASP.NET Core and EF Core logging now default to Warning, so the console isn't flooded.

## Found but not fixed
- **Duplicate reading POSTs:** if the app retries a `POST /readings`, it creates a duplicate reading and possibly a duplicate alert. Fixing it needs a client-supplied idempotency key or an agreed dedupe rule.
- **Alert delivery is not exactly-once:** if the care team accepts an alert but the response is lost or the database commit fails, it will be resent. That can't be closed without an idempotency key on their side.
- **A permanently failing alert** (for example a 4xx from the care team) is retried every minute forever. It needs a retry limit or a dead-letter state, which means a schema change.
- **National ID is not checked against the patient:** it comes from the request body and is only checked for format. Whoever has a valid token can submit any national ID, and the alert goes out under it. The token should carry it, or it should be looked up.
- **Meter text such as `HI` or `LO` is rejected with a 400:** those are the most clinically urgent readings. They need a product decision.
- **`appsettings.json` has a placeholder database password:** production must override it from a secret.
- **Photos are unscanned and unencrypted:** they sit on a local volume, so multiple instances need shared storage.
- **No EF migrations:** the tests build the schema from the model, and production has no migration setup.