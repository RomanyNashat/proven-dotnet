I fixed the problems listed below. The service builds with 0 warnings, the test project passes (51 of 51), and neither project has any known-vulnerable package, direct or transitive. Nothing that needs PostgreSQL has been run, so the database queries are untested, including the new per-clinician filter on `GET /results` and the page cursor.

## Problems fixed

| # | File | What | Why |
|---|---|---|---|
| 1 | `Program.cs` | Keycloak roles now become real role claims: the nested `realm_access.roles` and this client's `resource_access.lab-results.roles` are copied out when the token is validated (new `Infrastructure/KeycloakRoles.cs`). | No claim is literally named `realm_access.roles`, so the `clinician` policy turned away every real Keycloak user with a 403. |
| 2 | `Program.cs`, deleted `Infrastructure/AuditInterceptor.cs`, `Domain/LabResult.cs` | The clinician's `sub` is now passed straight into `LabResult.Record`, and an empty one throws. | The pooled DbContext got a request-scoped interceptor from the root service provider, so it was built once. Every result was then stamped with whoever came first, or with `"system"`. |
| 3 | `Infrastructure/CurrentUser.cs` | Reads the `sub` claim and throws if there isn't one, instead of falling back to `"system"`. `MapInboundClaims = false` keeps Keycloak's claim names. | A shared fallback identity hides bugs and would mix different users' data together. |
| 4 | `Api/ResultEndpoints.cs` | `GET /results` returns only the caller's own results (`RecordedBy == sub`). | Any clinician could page through every patient's results (broken object-level authorization). |
| 5 | `Api/ResultEndpoints.cs`, new `Api/ResultCursor.cs` | `next` is now an opaque cursor of (time, id), sorted by `ReceivedAt` then `Id`. It fetches one extra row so the last page returns `next: null`. A bad `before` returns 400. | A time-only cursor skips results that share a timestamp. `DateTimeOffset.Parse` crashed with a 500 on bad input. A `+03:00` offset broke Npgsql's `timestamptz` write. The old code also handed out a cursor that led to an empty page. |
| 6 | `Domain/LabResult.cs` | `ReceivedAt` is converted to UTC and cut to microseconds. | PostgreSQL stores microseconds, so the time in the `POST` response didn't match the one `GET` returned, and cursor comparisons were off. |
| 7 | `Api/*Validator.cs` (FluentValidation) | Every request body is validated: national ID is exactly 10 ASCII digits, phone is E.164, patient id > 0, and test, unit and notes have length limits. Value must fit `numeric(10,3)`. | Before, only test and unit were checked, and all errors were reported under `Test`. |
| 8 | `LookupRequestValidator.cs` | The lookup checks the national ID before it is put in the lab URL. | `".."` passes through `Uri.EscapeDataString`, so a caller could reach other paths on the lab API using our credentials. |
| 9 | `Infrastructure/LabResultConfiguration.cs` | `Notes` gets `HasMaxLength(1000)`. The id is `GENERATED ALWAYS AS IDENTITY`. A new index on (RecordedBy, ReceivedAt desc, Id desc) serves the list query. | `Notes` was an unbounded `text` column. |
| 10 | `Api/ResultEndpoints.cs` | The SMS no longer names the test, and the log line no longer contains the national ID, only the internal patient id. | The name of a test (HIV, pregnancy, …) in an SMS is health information. The national ID mask only worked for exactly 10 digits, so anything else was logged in full. I deleted `Masking.cs` because nothing used it any more. |
| 11 | `Program.cs` | The lab client no longer logs request URLs (`RemoveAllLoggers`). | The default HttpClient logging writes each URL at Information level, and the lab URL path contains the national ID. |
| 12 | `Application/NotificationWorker.cs` | The worker catches any failure, logs it without the phone number, and carries on. | A send timeout throws `TaskCanceledException`, not `HttpRequestException`. That ended the loop for good and, by default, stopped the whole service. |
| 13 | `Infrastructure/LabClient.cs` | When the lab returns 404 (patient not known), the lookup returns an empty list. | Before, a 404 became a 500. |
| 14 | `Program.cs`, new `ServiceEndpointOptions.cs` | The lab and SMS settings are typed and checked at startup. Each call has a 10-second timeout instead of 100 seconds. | Fail at startup on bad config, and don't tie up a request for 100 seconds. |
| 15 | `Program.cs` | Added global exception handling with ProblemDetails, HSTS, security headers (`nosniff`, frame deny, CSP, `no-store`), a 30-second clock skew for tokens, and a DbContext pool size of 128. | Errors no longer return empty bodies, and these are the house security defaults. |
| 16 | new `Directory.Build.props` | Turns on NuGet vulnerability auditing for direct and transitive packages, and makes known vulnerabilities and nullable warnings build errors. | House rule: zero known-vulnerable packages. |

I also split the files that held more than one type, one type per file. Everything the hidden tests rely on is unchanged: `Program`, the routes and their JSON shapes, `LabDbContext`, the typed clients, `TimeProvider`, the JWT setup, and the setting names.

## Found but not fixed
- **The notification queue is in memory.** Queued SMS are lost on restart. Also, if the caller cancels after the result is saved, the SMS is lost too. The fix is an outbox table, which is a design change.
- **No check that a clinician may treat a given patient.** Any clinician can record a result for any `patientId` or look up any national ID. There is no care-team data to check against.
- **The phone number comes from the request**, not from the patient's record, so a clinician can send the SMS to any number.
- **No HTTPS redirect.** Behind a load balancer that ends TLS, the service also needs forwarded-headers setup before HSTS takes effect. That depends on the deployment.
- **No retries or circuit breaker** on the lab and SMS calls. Retrying the SMS POST could send the message twice.
- **No migration scripts in the repo.** Under the house rules they must be generated as reviewed SQL scripts, not applied by the app.
- **Work journal not written.** Permission to write `.claude/journal.md` wasn't granted, so I skipped it.

## Tests added
**33 test methods (51 test cases)** in `LabResults.Tests/`, using xUnit and FluentAssertions held below 8.0. Eight of them run the real service in-process in Production with test-signed, Keycloak-shaped tokens and no database. They cover role mapping, 401/403/400 responses, path traversal, the cursor, security headers, and that the database model has no unbounded columns.
