I reviewed the service and fixed 12 problems, and it builds with 0 errors and 0 warnings. I added no tests, and nothing was run against PostgreSQL, so none of this has been exercised against a database or a real token.

## Fixed
1. **`Program.cs` – role claim.** `RoleClaimType = "realm_access.roles"` can't match anything, because Keycloak nests roles as `realm_access: { roles: [...] }`. That meant every clinician got 403. The new `Infrastructure/KeycloakRoles.cs` flattens them into role claims in `OnTokenValidated`.
2. **`Program.cs` – claim mapping.** I set `MapInboundClaims = false` and `NameClaimType = "sub"`, so `sub` stays `sub`.
3. **`Program.cs` – pooled DbContext.** `AddDbContextPool` was combined with a scoped `AuditInterceptor` and `CurrentUser`. The pool resolves the interceptor once and reuses it, so `RecordedBy` could be stamped with the wrong clinician. It's now `AddDbContext`.
4. **`CurrentUser.cs` and `AuditInterceptor.cs` – audit attribution.** The user used to fall back silently to `"system"`. Saving now throws if there is no user. I also stamp the synchronous `SavingChanges` path, which was missed before.
5. **`ResultEndpoints.cs` – paging.** The cursor was `ReceivedAt` alone, so results sharing a timestamp were skipped or repeated at page boundaries. The cursor is now `timestamp|id` with a stable `ThenByDescending(Id)`. A malformed `before` now returns 400 instead of a 500, and offsets are normalised to UTC.
6. **`ResultEndpoints.cs` – validation.**
   - Phone is now required and capped at 20 characters.
   - `value` is range-checked, because the column is `numeric(10,3)` and an out-of-range value caused a database error and a 500.
   - Errors now come back per field.
   - `lookup` requires a `nationalId`.
7. **`ResultEndpoints.cs` – SMS enqueue.** After the result was saved, `WriteAsync` could block on a full queue or throw on cancellation. The client then saw a failure even though the data was stored. It is now `TryEnqueue`, which logs a warning and drops the message if the queue is full.
8. **`NotificationQueue.cs` – SMS worker.** It only caught `HttpRequestException`. An SMS timeout (`TaskCanceledException`) or any other exception would crash the background service and, by default, stop the whole host. It now catches everything except shutdown cancellation.
9. **`Program.cs` – HTTP clients.** I set a 10-second timeout on `LabClient` and `SmsClient`, down from the 100-second default.
10. **`LabClient.cs` and `ResultEndpoints.cs` – lab errors.** A 404 from the lab now gives an empty list, and other upstream failures return 502 instead of an unhandled 500.
11. **`Masking.cs` – log masking.** It only masked exactly 10 consecutive digits, so a national id with spaces, dashes or a different length was logged in full. It now masks everything except the last four characters.
12. **`ResultEndpoints.cs` – cleanup.** A two-line cosmetic change; you can ignore it.

## Found, not fixed
- **Lost SMS.** The queue is in memory, so a restart or a failed send loses the message, and there are no retries. Fixing it properly needs an outbox table or a real queue.
- **No patient scoping.** Any clinician can list every result. That is likely by design, but check it against your privacy requirements.
- **Phone format.** It is only length-checked, not validated as E.164.
- **`Location` header.** `POST /results` returns `/results/{id}`, but there is no GET route for that path.
- **Connection string.** `appsettings.json` has no password. Supply it through environment variables or a secret store.
- **Database schema.** There are no migrations. The tests create the schema from the model, but production needs a migration step.
- **Raw PHI.** The raw `nationalId` and `phone` are accepted in request bodies. Make sure no HTTP logging or tracing captures those bodies.
