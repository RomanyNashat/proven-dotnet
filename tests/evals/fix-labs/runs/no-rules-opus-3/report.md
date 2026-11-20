# LabResults review

The service builds. `LabResults.Tests/` holds 21 tests (xunit). They run `WebApplicationFactory<Program>` in Production, with EF InMemory standing in for PostgreSQL, self-signed JWTs, stubbed lab and SMS servers, and a fake clock. All 21 pass. Nothing has been tested against a real PostgreSQL database.

## Fixed

### Security and authorization
1. **`Program.cs` / new `Infrastructure/KeycloakJwt.cs`: the `clinician` role never matched, so every endpoint returned 403.**
   - The code used `RoleClaimType = "realm_access.roles"`, but Keycloak sends `realm_access` as one nested JSON claim, so no claim of that type ever exists.
   - An `OnTokenValidated` handler now turns `realm_access.roles` and `resource_access.lab-results.roles` into role claims.
   - `MapInboundClaims = false`, so `sub` stays `sub`.
   - Settings are read through `IConfiguration` when the options are built, so test or environment overrides of `Auth:Authority` apply.
   - The authority, the `lab-results` audience and the default scheme are unchanged.

2. **`Infrastructure/AuditInterceptor.cs` and `CurrentUser.cs` (deleted): `recordedBy` could hold the wrong clinician.**
   - The DbContext is pooled (`AddDbContextPool`), so its options and interceptor are built once from the root provider.
   - The scoped `CurrentUser`/`AuditInterceptor` captured whichever user was signed in when the pool was first used, or `"system"`. Every later result was stamped with that person.
   - The interceptor is now a singleton that reads `sub` from the current `HttpContext` at save time.
   - It refuses to save if there is no authenticated subject, instead of storing a placeholder.
   - It now also overrides the synchronous `SavingChanges`.

3. **`Api/ResultEndpoints.cs`: the SMS named the test.** For example "HIV-1 RNA result ready" could show on a lock screen or be read by whoever holds the phone. The text is now generic: "Your lab result is ready, view it in the app" (in Arabic).

4. **`Program.cs`: national IDs went into the logs.** The default `IHttpClientFactory` logging writes request URIs at Information level, and the lab URI is `patients/{nationalId}/orders`. `ConfigureHttpClientDefaults(b => b.RemoveAllLoggers())` turns that off.

5. **`Api/ResultEndpoints.cs`, `Application/Masking.cs`: other national-ID leaks.**
   - The "result recorded" log line now carries the internal `PatientId` instead of the national ID.
   - `Masking.NationalId` used to pass anything that isn't 10 digits through unmasked, and partly masked 11+ digit strings. It now returns `***` for anything except exactly 10 digits.
   - `Notification.ToString()` hides the phone number.

6. **`Api/ResultEndpoints.cs`: path traversal on the lab API.** `nationalId` went into the lab URL path unvalidated. `Uri.EscapeDataString` leaves `..` alone, so `..` sent the call to a different lab endpoint. Lookup and record now require exactly 10 digits.

### Correctness
7. **`Api/ResultEndpoints.cs`, `LabDbContext.cs`: paging lost results and broke on some cursors.**
   - The cursor was `ReceivedAt` alone with `<`, so results sharing a timestamp at a page boundary were skipped.
   - Paging is now keyset on `(ReceivedAt, Id)`, using the cursor `yyyy-MM-ddTHH:mm:ss.fffffffZ_{id}`. It has no `+`, which an unescaped query string turns into a space.
   - The index is now on `(ReceivedAt, Id)`.
   - The query fetches `take + 1` rows, so the last page returns `next: null` instead of a cursor to an empty page.
   - A bad `before` returns 400. It used to throw `FormatException`, which gave a 500.
   - A non-UTC timestamp used to make Npgsql throw (another 500). Bare timestamps (the old format) are still accepted, converted to UTC.

8. **`Api/ResultEndpoints.cs`: `receivedAt` precision.** PostgreSQL stores microseconds, so the timestamp is truncated to microseconds before saving. That way the POST response matches later reads and cursors.

9. **`Api/ResultEndpoints.cs`, `Domain/LabResult.cs`: input validation.** These all used to produce a 500 or bad data:
   - A missing `nationalId` threw a NullReferenceException in `Masking`.
   - Values outside `numeric(10,3)` overflowed the column.
   - Values with more than 3 decimals were silently rounded, which changes a clinical value.
   - Phone numbers weren't checked; they must now be E.164 (`+9665…`, per LABS.md).
   - `patientId` could be 0 or negative.
   - `notes` had no length limit; it is now capped at 2000, and the column has the matching max length.
   - Errors are now reported per field.

10. **`Application/NotificationQueue.cs`: one SMS failure could stop the whole service.** The worker caught only `HttpRequestException`. An HttpClient timeout (`TaskCanceledException`) or any other exception escaped `ExecuteAsync`, and on .NET 8+ that stops the host. Now:
    - every failure is caught and logged;
    - each SMS gets up to 3 attempts with backoff (on `TimeProvider`);
    - shutdown is handled cleanly.

11. **`Api/ResultEndpoints.cs`, `NotificationQueue.cs`: a full SMS queue could duplicate results.**
    - When the queue was full (1000), `WriteAsync(…, requestToken)` blocked the request after the result was already saved.
    - If the client then gave up, the request failed even though the result was stored, so a retry recorded it twice.
    - Queuing is now `TryWrite`: if the queue is full, it logs and drops the SMS.

12. **`Infrastructure/LabClient.cs`, `ResultEndpoints.cs`: lab failures were 500s.**
    - Any non-2xx, network error, timeout or bad JSON from the lab became an unhandled exception.
    - A 404 (unknown patient) now returns an empty list. Other failures return a 502 problem response and a log line with the ID masked.
    - The lab client has a 15 s timeout instead of 100 s, and so does the SMS client.

13. **`Program.cs`: base addresses without a trailing slash.** A base address like `…/api` silently dropped the `api` segment from relative paths. A trailing slash is now added, and a missing setting fails with a clear message.

14. **`Program.cs`: Production error handling.** Added `AddProblemDetails`, `UseExceptionHandler` and `UseStatusCodePages`, so errors come back as RFC 7807 responses with no detail leaked.

15. **`Api/ResultEndpoints.cs`: dangling `Location`.** `POST /results` returned `Location: /results/{id}` but that route didn't exist. Added `GET /results/{id}` (clinician only).

## Found, not fixed
- **SMS are kept only in memory.** Queued messages are lost on restart or crash, and dropped when the queue is full or after 3 failures. The proper fix is a transactional outbox table. I left it because it adds schema, keeps phone numbers in the database, and changes the design.
- **No per-patient access control.** Every clinician can list every patient's results through `GET /results` and look up any national ID. The route shape is fixed, and this needs a product decision (care-team scoping, audit logging of reads).
- **No retries against PostgreSQL** (`EnableRetryOnFailure`), and no readiness check for the database.
- **No `UseForwardedHeaders`, HTTPS redirection or HSTS.** These depend on how it's deployed behind a proxy.
- **No rate limiting** on the lookup or SMS-triggering endpoints.
- **No authentication to the lab network or the SMS gateway.** LABS.md doesn't mention any. If they need credentials, those must be added.
- **No EF migrations.** The schema exists only in the model; the hidden tests create it with `EnsureCreated`. Production needs migrations. My model changes (index on `(ReceivedAt, Id)`, a 2000 limit on `Notes`) must go into the first migration.
- **`nationalId` is required to be exactly 10 digits.** That fits Saudi IDs and Iqama numbers. If other formats are expected, loosen the rule but keep it digits only.
- **I couldn't confirm that the log-leak test fails when the fix is removed.** I tried to temporarily remove the logging fix and rerun the test, but that needed approval, so I skipped it.
