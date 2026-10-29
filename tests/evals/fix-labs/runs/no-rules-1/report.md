I fixed nine problems and the service builds with no warnings. Nothing was run: I didn't add tests, and anything that needs PostgreSQL couldn't run here.

## Fixed

1. **`Program.cs`: `AddDbContextPool` changed to `AddDbContext`.**
   - The `AuditInterceptor` depends on the scoped `CurrentUser`. A pooled context keeps the interceptor it first got, so later requests would have been stamped `RecordedBy` with the first clinician's id. That corrupts the audit trail.
2. **`Program.cs`: Keycloak roles were never read.**
   - `RoleClaimType = "realm_access.roles"` doesn't work, because Keycloak nests roles in a JSON claim. Every clinician would have got 403.
   - I now read `realm_access.roles` in `OnTokenValidated` and add them as role claims.
   - I also set `MapInboundClaims = false`, with `sub` as the name claim.
3. **`CurrentUser.cs`: no more silent `"system"` fallback.**
   - A missing identity is now an error. Before, it would have written results attributed to "system".
4. **`ResultEndpoints.cs`: pagination skipped or repeated results.**
   - The cursor was just a timestamp, so rows sharing a `receivedAt` were dropped or repeated at page boundaries. A bad `before` value also gave a 500.
   - The cursor is now `ticks.id`, with `Id` as the tiebreaker in the ordering and in a composite index in `LabDbContext`.
   - A malformed cursor returns 400.
   - The `next` format changed, but it is opaque and passed straight back as `before`.
5. **`ResultEndpoints.cs`: input validation was too thin.**
   - A null `nationalId` or `phone` caused a null-reference 500 in `Masking`.
   - A value outside `numeric(10,3)` caused a database error.
   - `notes` was unbounded.
   - There are now field-level 400s for each.
   - `nationalId` must be 10 digits, which `Masking` assumes.
   - `phone` must be in international `+…` format.
6. **`ResultEndpoints.cs`: lab lookup failures returned a raw 500.**
   - Failures, timeouts and bad JSON from the lab network now return 502. The `nationalId` is also validated before the call.
7. **`Program.cs`: the national id was leaking into logs.**
   - The default `HttpClient` logging writes request URLs, and the lab URL contains the national id. I removed those loggers with `RemoveAllLoggers()` on both clients.
   - I also set a 10-second timeout on both clients. The default is 100 seconds, which would stall the single SMS worker.
8. **`NotificationQueue.cs`: the SMS worker could crash the host.**
   - It only caught `HttpRequestException`. A gateway timeout would have stopped the background service, and with it all SMS.
   - It now catches and logs any exception except shutdown.
9. **`NotificationQueue.cs`: a full queue blocked requests.**
   - When the 1000-slot queue filled up, `POST /results` hung even though the result was already saved.
   - Enqueueing is now non-blocking, and a dropped SMS is logged as a warning.

I also added `HasMaxLength(2000)` for `Notes` in `LabDbContext`.

## Found but not fixed
- **No EF migrations.** The tests build the schema from the model, but production needs a migration or schema script.
- **SMS is fire-and-forget.** The queue is in memory with no retry, so SMS can be lost on a restart or after a gateway error. Fixing that properly needs an outbox table or a real queue.
- **No idempotency on `POST /results`.** A client retry creates a duplicate result and sends a second SMS.
- **Any clinician can list all results.** `GET /results` isn't scoped to a patient or facility. That is a product decision.
- **The SMS text includes the test name.** That is mildly sensitive and goes out over SMS.
- **The connection string in `appsettings.json` has no password.** It has to come from environment configuration or a secret store in production.
- **The `before` filter and the `PatientId` index.** `PatientId` is indexed, but nothing queries by it yet.
