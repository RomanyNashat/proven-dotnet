I fixed the problems below in place. `ClinicReminders/` builds with 0 errors. My three unit tests pass, in `ClinicReminders.Tests/`. They cover the Arabic message text, the cursor parsing and the due-reminder window. Nothing that needs SQL Server was run, so the endpoints, the Quartz job end to end and the schema are untested.

## Fixed

**Sending and duplicates** (the SMS provider has no idempotency key)
- **`Application/SendDueRemindersJob.cs`:** `SaveChanges` ran once after the whole batch. A crash mid-batch lost the "sent" marks, so the next run re-texted patients. It now saves after every send.
- **`Application/ReminderService.cs`:** only `HttpRequestException` was caught. An SMS timeout (`TaskCanceledException`) killed the whole batch. It is now caught per reminder, which stays unsent and is retried on the next run. A real shutdown cancellation still propagates.
- **`Application/DueReminders.cs`, `Api/Program.cs`:**
  - This was a singleton holding a scoped `DbContext`, and the comment about "keeping reminders in memory" described caching it didn't do. It is now scoped and has no cache.
  - Its query had no lower bound, so reminders for appointments already past were sent.
  - The window was "within 24h of now". A 09:00 run gave someone with a 09:30 appointment 30 minutes' notice. It now covers today (still ahead) and tomorrow in Riyadh time, ordered by appointment.
- **`Api/Program.cs`:**
  - The `SmsClient` base address was `.../api/v2` with no trailing slash, so `"messages"` resolved to `/api/messages`. I fixed the slash, made the URL configurable (`Sms:BaseUrl`) and added a 15s timeout.
  - The cron `0 0 9 * * ?` ran in server time, which is usually UTC (12:00 in Riyadh). It now runs in `Asia/Riyadh`.
  - The Quartz persistent SQL Server store needs `QRTZ_*` tables that `EnsureCreated` never creates, so the scheduler would fail at startup. I switched to the in-memory store, since the job and trigger are defined in code, and removed the unused serializer package.

**Arabic message and Riyadh time**
- **`Application/ReminderService.cs`:**
  - `ar-SA` formatting uses the Hijri calendar by default.
  - The Dockerfile sets invariant globalization, which would turn day and month names into English.
  - The text is now built by hand with Arabic Gregorian day and month names in Riyadh time (`MessageText`, tested).
  - `Reference()` now uses the Riyadh date instead of the UTC date.
- **`Dockerfile`:** the alpine runtime image has no tz database, so `FindSystemTimeZoneById("Asia/Riyadh")` would throw. I added `apk add tzdata`.

**API**
- **`Infrastructure/ReminderQueries.cs`:**
  - Paging used `CreatedAt` alone as the cursor, so rows with the same timestamp were skipped at page boundaries.
  - The ISO cursor contained `+`, which a query string turns into a space, so it failed to parse.
  - `next` was non-null on a full last page.
  - The cursor is now `<utcTicks>.<id>` with `(CreatedAt, Id)` ordering, and it fetches `size+1` rows so `next` is null on the last page. A bad cursor returns 400 instead of 500.
- **`Api/ReminderEndpoints.cs`:**
  - `POST` had no validation. A mobile over 20 characters was a SQL truncation 500. It now returns 400 for a bad `patientId` or `mobile`.
  - `HttpCurrentUser` threw a `NullReferenceException` if the claim was mapped to `NameIdentifier` instead of `sub`. It accepts both and throws `UnauthorizedAccessException` if neither is numeric.
- **`Infrastructure/RemindersDbContext.cs`:** the indexes now match the queries: `(PatientId, CreatedAt, Id)` and `(SentAt, AppointmentAt)`.

## Found, not fixed
- **Who can schedule for whom:** `POST` accepts any `patientId` from any authenticated caller, while `GET` lists reminders whose `PatientId` equals the caller's `sub`. Either callers are patients and `POST` should force `patientId = sub`, or they are staff and `GET` is wrong. I can't tell which from the code, and a change could break your hidden tests.
- **Duplicates that remain:**
  - If the SMS is delivered but the response times out, or the save fails after a send, the reminder is re-sent on the next run. Without an idempotency key this can't be closed.
  - `POST` has no idempotency, so a client retry creates a second reminder.
  - Running two instances would double-send, because the in-memory Quartz store has no clustering. Run one instance, or add a database-level claim.
  - If the app is down at 09:00, that day's run is skipped.
- **Schema:** `EnsureCreated` at startup isn't a migration strategy. Use EF migrations before the schema first changes.
- **SMS authentication:** the SMS client sends no credentials, so production probably needs an API key header or similar.
- **JWT config:** `AddJwtBearer()` has no explicit options. It relies on `Authentication:Schemes:Bearer` in configuration, and production fails every request if that isn't set.
- **Pkcs advisory:** `System.Security.Cryptography.Pkcs` (a transitive dependency) is flagged, but the 10.0.0 version I tried is also flagged, so I left it alone. Look at it when a patched version ships.
- **Logging:** the patient identifier is erased in `ReminderLog`. I assumed Serilog sees the redacted value but did not verify it. Check the `Serilog` configuration before go-live.