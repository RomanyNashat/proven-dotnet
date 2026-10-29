I fixed 15 problems in the service. It builds with 0 warnings and 0 errors. The new test project `LabResults.Tests/` passes all 60 test cases (28 test methods). None of them need PostgreSQL. Nothing was run against a real database or Keycloak.

## Problems fixed

| # | File | What | Why |
|---|---|---|---|
| 1 | `Program.cs`, `Infrastructure/AuditInterceptor.cs` + `CurrentUser.cs` (deleted), `Domain/LabResult.cs`, `Api/ResultEndpoints.cs` | Removed the interceptor that set `recordedBy`. The endpoint now passes the caller's `sub` into `LabResult.Record(…, recordedBy, …)`, which rejects an empty value. | The pooled `DbContext` resolved the scoped interceptor once, from the root provider, and `CurrentUser` read the user when it was created. Every result was stamped `"system"` or with the first caller, so `recordedBy` was wrong. |
| 2 | `Program.cs`, new `Infrastructure/KeycloakRoles.cs` | When a token is validated, the roles in Keycloak's `realm_access.roles` are copied into role claims. `MapInboundClaims = false` keeps claim names like `sub` as they are. | `RoleClaimType = "realm_access.roles"` never matches, because Keycloak sends roles as a nested JSON object. Every clinician got 403. |
| 3 | `Program.cs` | The `clinician` policy also requires `sub`. `ClockSkew` is 30 seconds. `RequireHttpsMetadata` is set explicitly. | The old clock skew was the 5-minute default, and a token without `sub` must not be able to record results. |
| 4 | `Api/ResultEndpoints.cs` | `GET /results` returns only the caller's own results, and a new index on (`RecordedBy`, `ReceivedAt`, `Id`) supports that query. | Any clinician could page through every patient's results. Who recorded a result is the only ownership data the model has. |
| 5 | `Api/ResultEndpoints.cs`, new `Api/ResultCursor.cs` | The page cursor is now (`ReceivedAt`, `Id`), and results are sorted with `Id` as the tie-breaker. The query fetches one extra row, so the last page returns `next: null`. A malformed `before` gives 400. | With a timestamp-only cursor, results sharing a timestamp were skipped (likely with a fake clock). A bad `before` threw from `DateTimeOffset.Parse` and returned 500. |
| 6 | `Domain/LabResult.cs` | `ReceivedAt` is converted to UTC and truncated to microseconds. | PostgreSQL stores microseconds, so the `POST` response didn't match later reads or the cursor. Npgsql also needs offset 0. |
| 7 | `Api/ResultEndpoints.cs`, new `Api/RequestValidation.cs`, `Domain/LabResultLimits.cs` | Added DataAnnotations validation on both request bodies:<br>• patientId > 0<br>• national ID is 10 digits<br>• phone is E.164<br>• test, unit and notes have length limits<br>• value fits `numeric(10,3)` | Before, only test and unit were checked. A value too large for `numeric(10,3)` crashed the save with a 500. A lookup ID like `..` got through `EscapeDataString` and the lab URL resolved to a different path. |
| 8 | `Infrastructure/LabDbContext.cs` | `Notes` is now `varchar(1000)` (it was unbounded `text`). Ids use `GENERATED ALWAYS AS IDENTITY`. Lengths come from shared constants. | House rules ban unbounded columns. Sharing the constants keeps validation and the schema in step. |
| 9 | `Api/ResultEndpoints.cs` | The SMS no longer names the test. | It went to an unverified phone number and could reveal health data (e.g. "HIV"). |
| 10 | `Application/NotificationQueue.cs` | The worker now catches every failure except shutdown. | An HttpClient timeout (`TaskCanceledException`) got past the `HttpRequestException` catch and stopped the whole host. |
| 11 | `Application/NotificationQueue.cs`, `Api/ResultEndpoints.cs` | Queuing an SMS no longer waits: `TryEnqueue` is used, and a full queue logs a warning. | When the queue was full, the request hung after the result was already saved. A client retry then recorded the result twice. |
| 12 | `Application/Masking.cs` | Masking now works by length: only the last 4 characters show, and values of 8 characters or fewer are fully masked. | The 10-digit regex logged any other format of national ID in full. |
| 13 | `Program.cs` | `LabClient` logging is removed with `RemoveAllLoggers`. Both HTTP clients time out after 10 seconds. | HttpClient's log lines include the URL, which contains the national ID. The default timeout was 100 seconds. |
| 14 | `Api/ResultEndpoints.cs` | Lab network failures return a 502 ProblemDetails. | They were unhandled 500s. |
| 15 | `Program.cs` | Added ProblemDetails and an exception handler, HSTS, HTTPS redirection, security headers (nosniff, frame DENY, CSP), and a DB pool size of 128. | There was no error handling or header setup for Production. |

I also added `Directory.Build.props` at the folder root. It turns on NuGet audit for all packages, including transitive ones, and makes vulnerability warnings and nullable warnings fail the build. No vulnerable packages were found. FluentAssertions is pinned to `[7.2.0,8.0.0)` in the test project.

## Found but not fixed
- **No migration scripts.** The hidden tests build the schema from the model. Production needs reviewed up/down SQL scripts for the new `Notes` length, the identity change and the index.
- **The SMS queue is in memory.** Messages are lost on restart, and a failed send is logged but never retried. A durable outbox would fix this.
- **No encryption at rest.** Results and free-text notes are health data, and nothing encrypts them at rest (column- or disk-level).
- **Who can look up orders.** Any clinician can look up any national ID's orders, and there is no rate limit. That fits the documented design, but it may need auditing.
- **Unknown lab 404 behaviour.** `LABS.md` doesn't say what the lab returns for an unknown patient. If it is 404, users currently get a 502.
- **Clinicians only see their own results.** If the intended rule is "patients under my care" or a tenant, the model needs that relationship; filtering by who recorded the result is the only option the current data allows.
- **No work journal entry.** The house rules ask for one in `.claude/journal.md`, but writing that file was blocked as a sensitive file, so it was skipped.

**Unit tests added: 28 test methods (60 test cases including theory rows).**
