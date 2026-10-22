I fixed the problems below and the service builds with 0 warnings. I couldn't run anything against PostgreSQL here, and I didn't add any tests. The locking SQL, the transactions and the health checks are all untested.

## Fixed

**Security and privacy**
- **`Application/ReadingQueries.cs`, `GET /readings/{id}`:** it returned any patient's reading. It now filters on the caller's patient id, so another patient's id gives 404.
- **`Application/ReadingQueries.cs`, `/readings/latest`:** the cache key `readings:latest` was shared by all patients, so patient A could be shown patient B's latest reading. A new reading was also hidden for up to a minute. I removed the cache and added a tie-break on `Id`.
- **`Api/ErrorMiddleware.cs`:** it returned `ex.Message` and the inner exception message to the client, which leaks SQL and connection details. It now returns a generic body. `InvalidReadingException` gives 400 and `UnauthorizedAccessException` gives 403. It also no longer tries to write a response after the response has started or the client has disconnected.
- **`Application/ReadingService.cs`, logging:** it logged the whole reading with `{@Reading}`, which includes the national id and notes. It now logs the reading id and patient id only.
- **`Infrastructure/PhotoStore.cs`:**
  - It used the client's `FileName`, which allowed path traversal and overwrote earlier uploads. It now saves under a generated GUID name.
  - It now accepts only non-empty JPEG, PNG, HEIC or WebP files up to 10 MB, and deletes the previous photo when a new one replaces it.
  - It also fails at startup if `Storage:PhotoRoot` is missing.
- **`appsettings.json`:** I removed the committed `Password=change-me`. The password should come from `ConnectionStrings__Readings` in the environment or a secret store.

**Correctness**
- **`Application/ReadingService.cs`, value parsing:** `decimal.Parse(request.Value)` used the thread culture, which `LanguageMiddleware` set from `x-language`, so the same meter text could parse differently or fail. It now parses with the invariant culture. It also accepts Arabic-Indic digits and `,` or `٫` as the decimal separator, and bad input gives 400 instead of 500.
- **`Api/LanguageMiddleware.cs`:** it now sets only the UI culture, not `CurrentCulture`. `ar-SA` also changes the calendar and number formatting.
- **`Application/ReadingQueries.cs`, `/readings/today`:** it used the UTC day, which is wrong for Riyadh patients. It now uses the Riyadh day (UTC+3, which has no daylight saving).
- **`Application/ReadingService.cs`, `CollectedAt`:** Npgsql throws when writing a non-UTC `DateTime` to `timestamptz`, so an offset-less timestamp gave a 500. I now normalise it to UTC and treat an offset-less time as Riyadh wall-clock time. A time more than 10 minutes in the future is rejected.
- **`Application/ReadingService.cs`, input checks:**
  - The national id must be 10 digits. Before, an id longer than 10 characters failed at the database as a 500.
  - The unit must be `mmol/L` or `mg/dL`.
  - The value must be in range, and notes are capped at 1000 characters.
- **`Domain/Reading.cs`:** the alert threshold of 11.1 is in mmol/L, but the value was compared as sent, so any `mg/dL` reading triggered an alert. I added `ValueMmolL`, and both the alert check and the care-team payload use it.
- **`Application/ReadingService.cs`, `AddAsync`:** the reading and its alert were saved in two separate calls, so a crash in between lost the alert. They are now in one transaction.
- **`Api/CurrentPatient.cs`:**
  - It used `int.Parse` with a `!`, so a missing or non-numeric `sub` gave a 500. It now reads `sub` and returns 403 if it isn't a valid id.
  - `Program.cs` also sets `MapInboundClaims = false` and passes the default scheme explicitly.

**Care-team alerts (no idempotency key)**
- **`Program.cs`:** `AddStandardResilienceHandler` retries POSTs by default, which can page a nurse twice. I disabled retries for unsafe methods. I also made the base address end in `/` and fail clearly if it is missing.
- **`Application/ReadingNotifier.cs`:**
  - Each alert is now sent inside a transaction with `SELECT … FOR UPDATE SKIP LOCKED`, so two instances can't send the same alert.
  - An alert that fails is logged and left unsent, and the others in the batch still go out.
  - An exception in the background loop used to stop the whole host; the loop now logs it and carries on.

**Operations**
- **`Program.cs`, health checks:** `/health/live` also checked the database, so a database outage would restart healthy instances. Liveness now runs no checks, and only `/health/ready` checks the database.

## Found, not fixed
- **Poison alerts:** an alert that always fails is retried every minute with no limit or dead-letter handling. A crash or timeout after the care team receives the alert but before we mark it sent will still produce a duplicate, because they have no idempotency key.
- **Alert batch size:** only the oldest 50 alerts are sent per minute. The backlog shows up as unsent rows but isn't monitored.
- **National id source:** it comes from the request body and isn't checked against the token's patient. The contract requires that field, so it is only format-checked. I'd look it up from the patient's profile instead.
- **Latest reading:** `/readings/latest` is ordered by `CollectedAt`, so a late-synced older reading won't show as latest. That is probably intended.
- **Photo files:**
  - They sit on local disk with no cleanup if the database save fails, and with several instances they need a shared volume.
  - There is no content sniffing beyond the declared content type.
- **Missing infrastructure:** there are no EF migrations in the repo (the tests build the schema from the model), no rate limiting, and no HTTPS or forwarded-headers configuration.
- **Request size and Serilog:** no request-size limit is set beyond Kestrel's default 30 MB, and Serilog logs to the console only.
- **Care-team payload:** it doesn't include the unit. It now always sends mmol/L, and I assumed their system expects that.