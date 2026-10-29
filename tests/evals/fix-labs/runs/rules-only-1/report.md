I reviewed and fixed `LabResults/`. The service builds with no warnings or errors, the NuGet audit finds no vulnerable packages, and all 65 unit tests I added in `LabResults.Tests/` pass. I couldn't run the service directly because the command was blocked, so I booted it in-process in Production with `WebApplicationFactory` instead. The tests that need PostgreSQL were not run, since there's no database here.

## Problems fixed

**Security**
1. **`Program.cs`: Keycloak roles were never read, so every clinician got 403.** `RoleClaimType = "realm_access.roles"` doesn't match any real claim, because Keycloak sends roles nested as `realm_access: { roles: [...] }`. The new `Infrastructure/KeycloakRolesTransformation.cs` turns those into role claims, and it still works if tests or hosts replace `JwtBearerOptions`. I also set `MapInboundClaims = false`, `ClockSkew` to 30s, and kept the audience and authority as they were.
2. **`Program.cs` / `AuditInterceptor.cs` / `CurrentUser.cs`: `recordedBy` was wrong.** `AddDbContextPool` creates the scoped interceptor once, from the root provider. That fixes `CurrentUser` on whoever was signed in at that moment (or `"system"`), so results got the wrong clinician. The context is now `AddDbContext` (scoped). `CurrentUser.Id` reads `sub` each time and throws rather than saving a placeholder. The interceptor now covers synchronous `SaveChanges` too.
3. **`ResultEndpoints.cs`: `GET /results` returned every clinician's results.** This is a broken object-level authorization bug. The list is now limited to the caller's own results (`RecordedBy == sub`).
4. **`ResultEndpoints.cs` + `LookupRequestValidator.cs` + `Application/Identifiers.cs`: path traversal into the lab API.** `nationalId = ".."` gets through `Uri.EscapeDataString`. Lookup now accepts exactly 10 digits.
5. **`RecordResultValidator.cs`: almost no input validation.** It now checks patient id > 0, national id is 10 digits, the phone number is E.164 (as the SMS gateway expects), the length of test, unit and notes, and that the value fits `numeric(10,3)` (before, an oversized value caused a database error and a 500). Null fields return 400, not a crash.
6. **`Notification.cs`: the SMS named the test** ("نتيجة تحليل HIV…"). That puts health data on a lock screen or the wrong phone. The text is now generic.
7. **`Masking.cs`: any national id that wasn't exactly 10 digits was logged in full or partly.** It now always masks all but the last 4 characters, and the log line uses `PatientId` instead.
8. **`Program.cs`: no error handling or security headers.** I added `ProblemDetails` with the exception handler, HSTS, HTTPS redirection, and the `nosniff`, frame, CSP and `no-store` headers.

**Correctness**
9. **`ResultEndpoints.cs` + `Application/ResultCursor.cs`: paging skipped or repeated rows.** The cursor was only `ReceivedAt`, so results with the same timestamp broke it. It's now a keyset on (`ReceivedAt`, `Id`) with an opaque cursor. A bad `before` returns 400 instead of a 500 from `DateTimeOffset.Parse`, and the last page no longer returns a `next`.
10. **`Domain/LabResult.cs`: the POST response's `receivedAt` differed from the stored value.** It had 100ns precision while PostgreSQL keeps microseconds. The value is now truncated to microseconds in UTC.
11. **`NotificationWorker.cs`: one SMS timeout could stop the whole service.** It only caught `HttpRequestException`, and an HttpClient timeout throws `TaskCanceledException`. That ended the worker, which stops the host by default. It now logs any failure and carries on.
12. **`NotificationQueue.cs`: a full queue blocked the request.** The result was already saved, but the request waited on the SMS backlog. It now uses `TryEnqueue` and logs a warning if the queue is full.
13. **`Program.cs`: HTTP calls had the default 100s timeout.** Both clients now use 10s.
14. **`Program.cs` + `Infrastructure/RequiredSettings.cs`:** missing or malformed settings now fail at startup.

**Schema and build**
15. **`LabResultConfiguration.cs` (configuration moved out of `LabDbContext`):** `Notes` was an unbounded `text` column; it's now `varchar(1000)`. The key is now `GENERATED ALWAYS AS IDENTITY`. The index is now (`RecordedBy`, `ReceivedAt desc`, `Id desc`) to serve the list.
16. **`Directory.Build.props`:** a vulnerable NuGet package (direct or transitive) now fails the build, and nullable warnings CS8600/8602/8603 are errors.

## Found but not fixed
- **No patient–clinician link in the model.** So `POST /results` and `/lab-orders/lookup` can't check that the clinician actually treats that patient. Fixing this needs a domain decision.
- **SMS can be lost.** The queue is in memory, so a restart or full queue drops messages. A durable outbox would fix it.
- **No migration scripts in the repo.** The up and down SQL for the new column length, identity and index needs generating and review.
- **Endpoints use `LabDbContext` directly** instead of going through an application layer. This is about structure, not behaviour.
- **Lab API errors come back as a generic 500.** A 502 would be more accurate; LABS.md doesn't define the lab's error responses.
- **Base addresses aren't required to be HTTPS.** Tests may point them at local fakes.
- **`Created` returns `/results/{id}`, but there's no route for it.** I kept the response as it is.
- **The work journal wasn't written.** Permission to edit `.claude/journal.md` was denied.

**Unit tests added: 65** (in `LabResults.Tests/`, xUnit with FluentAssertions pinned to `[7.2.0,8.0.0)`).
