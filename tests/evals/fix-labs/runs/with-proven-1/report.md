I applied all 23 findings in the review. The service builds with 0 warnings, the 103 new tests pass, and `dotnet list package --vulnerable --include-transitive` reports nothing for either project. None of the database work could run here: the SQL queries, the outbox relay against PostgreSQL, and the schema itself are untested.

## Fixed (paths under `LabResults/`)
| # | File | What | Why |
|---|---|---|---|
| 1 | `Infrastructure/KeycloakRoles.cs`, `Program.cs` | Roles are copied out of `realm_access.roles` and `resource_access.lab-results.roles` in `OnTokenValidated` into `roles` claims. `MapInboundClaims = false`, still on the default JwtBearer scheme. | Every clinician got 403. |
| 2 | `Api/ResultEndpoints.cs`, `Domain/LabResult.cs` | `sub` is read from the caller in `RecordAsync`, which returns 401 if it is missing, and passed to `LabResult.Record(..., recordedBy)`. `AuditInterceptor` and `CurrentUser` are deleted; the pool stays. | The pool kept the first caller's `sub` and stamped it on every result. |
| 3 | `Infrastructure/SmsOutboxWorker.cs`, `SmsOutboxRelay.cs` | Failures are caught with `catch (Exception) when (!stoppingToken.IsCancellationRequested)`. Logs carry the exception type and status code only. | One SMS timeout stopped the whole host. |
| 4 | `Program.cs` | `.RemoveAllLoggers()` on `LabClient`; `LabClient` logs only the status code. | National IDs were logged from the URL path on every lookup. |
| 5 | `Application/Masking.cs` deleted | The log line now uses `PatientId`. Nothing else logs a national ID, so I removed the mask rather than leave unused code. | The mask let unusual IDs through whole. |
| 6 | `Api/*Validator.cs`, `Application/NationalId.cs`, `SaudiMobile.cs`, `Digits.cs` | FluentValidation on both request types. Arabic digits are converted first, then: national ID exactly 10 digits, Saudi mobile only, `PatientId > 0`, `Value` fits `numeric(10,3)`, `Notes` ≤ 1000. Each error is under its own key. | Path traversal to the lab API, SMS to any number, 500s from the database. |
| 7 | `Infrastructure/LabDbContext.cs` | `Notes` is `HasMaxLength(1000)`. | It was an unbounded `text` column. |
| 8 | `Program.cs` | Scope validation on in every environment. | This is what let finding 2 reach Production. |
| 9 | `Program.cs` | `ClockSkew` 30 s, `RequireHttpsMetadata = true`. | Security rules. |
| 10 | `Api/ResultCursor.cs`, `ResultEndpoints.cs`, `LabDbContext.cs` | Paging orders by time then id, with both in an opaque cursor. A bad cursor gets 400 and the time is converted to UTC. The index is now `(ReceivedAt, Id)`. | Rows sharing a timestamp were skipped; a bad cursor gave 500. |
| 11 | `Program.cs` | ProblemDetails, exception handler, status code pages, HSTS (1 year). `nosniff`, `X-Frame-Options`, CSP and `no-store` are set in `OnStarting`. | They now appear on errors too. |
| 12 | `Program.cs`, `Infrastructure/LabClient.cs` | Standard resilience handler on both clients (SMS POST is not retried). A lab 404 now returns an empty list. | No timeouts before; a 404 became a 500. |
| 13 | `Domain/PendingSms.cs`, new `sms_outbox` table, relay and worker | The SMS row is saved with the result in one `SaveChanges`. Only one pod sends at a time (advisory lock), with 5 attempts, and the phone is cleared once sent or given up. | The in-memory queue lost messages on restart and could store a result twice. |
| 14 | `Application/SmsText.cs`, `Resources/SmsText.resx` | One generic message from resources, Arabic, ≤ 70 characters. | The SMS named the test. |
| 15 | `Program.cs` | Lookup limited to 30 per minute per `sub`. | One token could walk through national IDs. |
| 16 | `appsettings.json` | `Maximum Pool Size=20;Timeout=15;Command Timeout=30`. | There is no password in the file, so nothing to rotate. |
| 17 | `LabResults/Directory.Build.props`, `LabResults.Tests/Directory.Build.props` | NuGetAudit, mode all, NU1901–1904 as errors. | Vulnerable packages now fail the build. |
| 18–23 | various | Keys are `ALWAYS` identity. `ReceivedAt` is cut to microseconds. 201 has no `Location`. Settings are validated at start-up. The `PatientId` index is dropped. The log category is `ResultEndpoints`. | |

## Tests
They are in `LabResults.Tests/` (xUnit, FluentAssertions 7.x pinned below 8, Moq). They cover the role mapping, validators, cursor, domain rules, the model (bounded strings, identity keys, indexes, `timestamptz`), the lab client, the SMS send step, and the worker surviving a timeout. The 401 and 400 paths of the endpoints are tested too. `ApiTests` starts the real host in Production with test-signed tokens and checks 401, 403, 200, 400 on traversal, 429, and the headers. I wrote the tests first and confirmed they failed (the types didn't exist yet). I didn't measure coverage.

## Found, not fixed
- **PHI read audit** for `GET /results` and lookup, and **care-team scoping**: the review left these for the product owner.
- **Migration scripts**: not generated. They need the new `sms_outbox` table, the index change (in its own `CONCURRENTLY` script), `notes` changed to `varchar(1000)` after checking for longer rows, and the identity change.
- **Risks for the hidden tests:**
  - With `RequireHttpsMetadata = true`, start-up fails if the tests set `Auth:Authority` to an `http://` address.
  - Phone numbers outside the Saudi formats now get 400.
  - The SMS worker reads the test database every 5 s.
- **Duplicate SMS**: after a gateway timeout the outbox can send the generic SMS twice, because the gateway has no idempotency key.
- **Phone numbers in `sms_outbox`** are not encrypted while they wait to be sent.
- **Forwarded headers** (for HSTS behind nginx) are not configured.
- **Work journal**: I couldn't write `.claude/journal.md`; the edit was refused as a sensitive file.
