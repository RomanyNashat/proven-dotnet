# LabResults review

The service builds with 0 warnings. `LabResults.Tests/` has 46 tests (unit tests plus in-process `WebApplicationFactory<Program>` tests for auth, roles and lab lookup, none of which need a database), and all of them pass. Nothing that touches PostgreSQL could be run here: the outbox worker, paging and the audit stamp are untested against a real database. I did check the generated schema script.

## Fixed

| # | File | Problem | Why it matters / fix |
|---|------|---------|----------------------|
| 1 | `Program.cs`, new `Infrastructure/KeycloakRolesTransformation.cs` | `RoleClaimType = "realm_access.roles"` never matches. Keycloak sends a single nested JSON claim, `realm_access: {"roles":[...]}`, so `RequireRole("clinician")` failed for every real token. | Every endpoint returned 403. An `IClaimsTransformation` now parses `realm_access.roles` into role claims on a separate identity. It runs only once per principal, ignores malformed claims, and doesn't depend on `TokenValidationParameters`, so tests can replace those freely. |
| 2 | `Program.cs`, `Infrastructure/AuditInterceptor.cs`, `Infrastructure/CurrentUser.cs` | `AddDbContextPool` builds its options once, from the root provider. The **scoped** `AuditInterceptor`/`CurrentUser` was therefore resolved once and reused. Production doesn't validate scopes, so this fails silently. `CurrentUser` also read the user in its constructor. | Every result would be stamped with whoever made the first request, or with `"system"`. `recordedBy` was wrong, which breaks the audit trail on medical records. Both classes are now singletons that read `HttpContext.User` at save time. A missing user now throws instead of being saved as `"system"`, and the sync `SavingChanges` is covered too. |
| 3 | `Program.cs`, `CurrentUser.cs` | The handler mapped claims, so `sub` became `ClaimTypes.NameIdentifier`. | `MapInboundClaims = false` keeps Keycloak's claim names, so `recordedBy` is read from `sub` (with `NameIdentifier` as a fallback). |
| 4 | `Api/ResultEndpoints.cs`, new `Api/PageCursor.cs`, `LabDbContext.cs` | The cursor used only `ReceivedAt` with `<`. | Results received in the same instant (bursts, or a frozen `TimeProvider`) were **silently skipped** across page boundaries. The cursor is now an opaque keyset cursor on `(ReceivedAt, Id)`, ordered by `ReceivedAt DESC, Id DESC`, and backed by a matching composite index. |
| 5 | `Api/ResultEndpoints.cs` | An invalid `before` value made `DateTimeOffset.Parse` throw, giving a 500. | It now returns 400 with a validation problem. |
| 6 | `Api/ResultEndpoints.cs` | `next` was set whenever a page was full. | When the total was an exact multiple of `limit`, clients were sent to an extra empty page. The query now fetches `limit + 1` rows to decide. |
| 7 | `Domain/LabResult.cs` | .NET times have 100 ns precision, PostgreSQL stores microseconds, and `Value` is stored as `numeric(10,3)`. | The POST response didn't match what GET later returned, and cursors built from in-memory values wouldn't round-trip. Times are now truncated to microseconds in UTC, and values rounded to 3 decimal places, before saving. |
| 8 | `Application/NotificationQueue.cs`, new `Domain/SmsMessage.cs`, `LabDbContext.cs` | SMS went through an in-memory `Channel`: | Replaced with a **transactional outbox** (`sms_outbox`), saved in the same `SaveChanges` as the result. The worker claims rows with `FOR UPDATE SKIP LOCKED` (safe with several instances), retries with exponential backoff up to 8 attempts, deletes rows once sent so phone numbers aren't kept, and is woken immediately after each insert. |
| | | • notifications were lost on every restart or deploy; | |
| | | • a full queue blocked the HTTP request; | |
| | | • a cancelled request could save the result with no SMS. | |
| 9 | `Application/NotificationQueue.cs` | The worker only caught `HttpRequestException`. A timeout (`TaskCanceledException`) or any other error ended `ExecuteAsync`. | Since .NET 8 that **stops the whole host** (`BackgroundServiceExceptionBehavior.StopHost`). The worker now catches and logs everything except shutdown, then keeps going. |
| 10 | `Api/ResultEndpoints.cs` | The SMS text included the test name ("your {Test} test result is ready"). | That is health data in an SMS, readable on a lock screen or by anyone holding the phone (HIV, pregnancy…). The text is now generic. |
| 11 | `Api/ResultEndpoints.cs`, `Application/Masking.cs` | The national ID was logged, "masked" by a regex that only matched exactly 10 consecutive digits. | Any other format (spaces, letters, other lengths) was logged in full. Logs now carry the internal `PatientId` only. `Masking.Identifier` keeps the last 4 characters of anything and fully masks short values. It is used for phone numbers in SMS failure logs. |
| 12 | `Program.cs` | The default `HttpClient` logging writes request URLs at Information level, and the lab URL is `patients/{nationalId}/orders`. | National IDs were written to the logs on every lookup. `.RemoveAllLoggers()` is now set on the `LabClient` builder. |
| 13 | `Api/ResultEndpoints.cs`, new `Api/Validation.cs` | Lookup accepted any `nationalId`. `Uri.EscapeDataString` leaves `.` alone, so `".."` resolved `patients/../orders` to a different lab endpoint. | That was **path traversal** against the lab API. IDs must now be 4–20 ASCII letters or digits, otherwise the request gets a 400. |
| 14 | `Infrastructure/LabClient.cs`, `ResultEndpoints.cs` | Any lab error, timeout or bad JSON caused an unhandled 500, and the default timeout was 100 s. | A 404 now returns an empty list. Other failures become `LabUnavailableException`, mapped to a 502 problem response, and the exception text never includes the URL. The timeout is 10 s (15 s for SMS). |
| 15 | `Api/Validation.cs`, `ResultEndpoints.cs` | `POST /results` validation was incomplete: | Each of these now returns a per-field 400. |
| | | • `Value` could overflow `numeric(10,3)` (a 500 from the database); | |
| | | • `Notes` had no length limit (unbounded column); | |
| | | • `Phone` was not checked, including null, so bad numbers only failed later in the worker; | |
| | | • `PatientId` was not checked. | |
| 16 | `LabDbContext.cs` | `Notes` had no maximum length. | Now `varchar(2000)`. |
| 17 | `Program.cs` | Settings were read eagerly from `builder.Configuration`, and a missing value gave `new Uri(null!)` or a null connection string. | Settings are now read from the provider's `IConfiguration` when first needed. Host and test-factory configuration is honoured, and a missing value fails with a clear message. |
| 18 | `Program.cs` | Production had no exception or status-code handling. | Added `AddProblemDetails`, `UseExceptionHandler` and `UseStatusCodePages`, so errors return RFC 7807 bodies instead of empty 500s and nothing leaks. |

## Found but not fixed

- **Any clinician can list every patient's results** (`GET /results` has no patient filter or per-patient authorisation), and **can look up any national ID** at the lab. This is the API shape the tests rely on, so I left it. Production needs a care-relationship check, or at least access auditing.
- **`nationalId` in `POST /results` is unused** now that it isn't logged. It is still accepted to keep the request shape.
- **SMS delivery is at-least-once.** A crash between the gateway accepting a message and the row being deleted resends it. The gateway API described in LABS.md has no idempotency key, so exactly-once isn't possible.
- **Messages the gateway rejects permanently** (for example a 4xx for an invalid number) are still retried up to 8 times before being dropped.
- **No schema migrations.** The schema comes only from the model (the tests use `EnsureCreated`-style creation). Production needs EF migrations, including for the new `sms_outbox` table, the `Notes` length and the changed index.
- **No retry or circuit breaker on the lab client.** Lab GETs are idempotent, so `Microsoft.Extensions.Http.Resilience` would be a good next step. I didn't add a package for it.
- **`Created` points to `/results/{id}`, but that route doesn't exist.** I kept it because the result body is what clients use.
- **No rate limiting** on the lookup endpoint, which proxies to an external lab network.
- **No forwarded-headers or HTTPS settings.** These depend on the deployment (reverse proxy or ingress).
- **Logging at the `Information` level still records every SQL statement EF executes.** Parameter values stay hidden unless `EnableSensitiveDataLogging` is on, and it is off.
