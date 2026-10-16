I read all 12 files and edited them in place. Nothing was built or run, so none of it has been compiled or tested. I checked each change against the APIs by reading only.

## Fixed

**Application/SendDueRemindersJob.cs, DueReminders.cs (deleted), Program.cs**
- **Reminders were never saved as sent.**
  - `DueReminders` was a singleton holding a scoped `DbContext`. It loaded entities into its own captive context, but the job saved through a different context. `SentAt` was never persisted, so every patient would have been re-texted every morning.
  - I deleted `DueReminders`. The job now queries directly with `AsNoTracking`, 1000 per run.
  - The query also had no lower bound, so it picked up every past unsent appointment. It now only takes `now < AppointmentAt < now+1d`.
- **One failure aborted the batch.** The job now catches per reminder (not cancellation), so one bad reminder doesn't stop the rest.

**Application/ReminderService.cs: duplicate SMS (the provider has no idempotency key)**
- Before, the service sent first and saved once at the end. A crash or failed save re-sent the whole batch, and concurrent runs or instances could both send.
- It now claims the row first with an atomic `UPDATE … WHERE SentAt IS NULL`, and sends only if it won the claim.
- If the provider answers with an HTTP error, the claim is released and the next run retries.
- On a timeout or dropped connection the claim stays, because the SMS may have been delivered. This is at-most-once delivery: a crash after the claim loses one reminder rather than texting a patient repeatedly.

**Program.cs, Dockerfile: Quartz**
- The persistent SQL Server job store needs `QRTZ_*` tables that nothing creates, so startup would have failed. I switched to the in-memory store. The atomic claim above is what protects against several instances firing at once.
- The cron `0 0 9` ran in the host timezone, which is UTC in the container, so reminders went out at 12:00 Riyadh. It now runs in Riyadh time.

**Application/ReminderService.cs, Dockerfile: Arabic text and timezone**
- The alpine image has no `tzdata` or ICU. With `GLOBALIZATION_INVARIANT=true`, `new CultureInfo("ar-SA")` throws, and `FindSystemTimeZoneById("Asia/Riyadh")` throws in a static initializer.
- The Dockerfile now installs `icu-libs` and `tzdata` and turns invariant mode off. I also added a restore layer for caching.
- `ClinicTime.Riyadh` falls back to a fixed UTC+3 zone, which is exact because Saudi Arabia has no DST.
- `ar-SA` formats dates in the Hijri calendar by default, which would put a Hijri date in the SMS. The text is now forced to Gregorian.

**Application/ReminderLog.cs, Program.cs, csproj: patient phone numbers in logs**
- `UseSerilog` replaces the logger factory, so the redaction setup almost certainly never applied (I couldn't confirm that without running it). Phone numbers were being logged.
- I stopped logging the mobile number and removed the redaction packages and setup.

**Infrastructure/SmsClient registration (Program.cs): wrong SMS URL**
- The base address `…/api/v2` had no trailing slash, so the relative `messages` path resolved to `/api/messages`.
- The base URL now comes from `Sms:BaseUrl`, with a trailing slash enforced. I also added a 15s timeout.

**Infrastructure/ReminderQueries.cs: paging**
- The cursor was `CreatedAt` only, so rows with the same timestamp were skipped.
- The cursor was also an ISO string whose `+` is mangled in a query string, and a bad value caused a 500.
- It now uses keyset paging on `(CreatedAt, Id)` with a URL-safe cursor `{ticks}-{id}` and returns 400 for a bad cursor.
- It fetches `size+1`, so `next` is null on the last page even when that page is exactly full.
- I removed the unused `MarkSentAsync`.

**Infrastructure/RemindersDbContext.cs: indexes**
- The list index is now `(PatientId, CreatedAt, Id)`.
- I added `(SentAt, AppointmentAt)` for the job query.

**Api/ReminderEndpoints.cs, Program.cs: input and auth**
- `POST /reminders` now validates `patientId` > 0 and `mobile` against `^\+?\d{7,15}$`, and returns 400 on failure. Before, an over-long mobile hit the 20-character column limit and caused a 500.
- The 201 response now includes `{ id }`.
- `HttpCurrentUser` read the `sub` claim with `!` and `int.Parse`. It now also accepts `NameIdentifier` and throws a clear error if the claim isn't numeric.
- `AddJwtBearer()` had no `Authority` or `Audience`. Both now come from `Authentication:Jwt:Authority` and `Authentication:Jwt:Audience`, which you must set in production.
- Configuration is read lazily, so test overrides of `ConnectionStrings:Reminders` are honoured.

**Cleanup**
- I removed the unused `Reference()` method, `MarkSent` and the `Quartz.Serialization.SystemTextJson` package.

## Found but not fixed
- **Authorization:** `GET /reminders` lists by the caller's `sub`, treating the user id as a patient id. `POST` lets any authenticated caller create a reminder for any `patientId`. I can't tell whether callers are patients or staff, so I left it. It needs a decision.
- **No audit on reads:** reading reminders (patient data) isn't audited, only creating them is.
- **Schema:** `EnsureCreatedAsync` at startup is not a production schema strategy. There are no migrations, it races across instances, and it can't evolve the schema. I couldn't generate migrations without a build.
- **Send window:** a rolling 24 hours from 09:00 means a 09:30 appointment is texted 30 minutes beforehand. A "tomorrow in Riyadh" window may be what you want, but that is a product decision.
- **Retries and SMS auth:**
  - I added no retry or resilience handler on the SMS call, because retrying without an idempotency key causes duplicate texts.
  - The SMS call carries no API key or auth header. That presumably belongs in configuration for the real provider.
- **Past appointments:** the API still accepts appointments in the past, which will simply never be reminded.
- **Dependencies:** package versions, such as the Quartz 3.15.0 pin and `Microsoft.Data.SqlClient` 6.1.6, are unverified because I had no NuGet access.