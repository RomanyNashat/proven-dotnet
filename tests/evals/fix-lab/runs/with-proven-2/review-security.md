## Security Review: HomeReadings (security-reviewer, read-only)

The four Critical findings each leak patient data or corrupt clinical alerts. I would not ship until they are fixed.

### Critical
1. **A01 IDOR.** `Application/ReadingQueries.cs:12-16` (via `Api/ReadingEndpoints.cs:17`). `GetAsync` filters on `Id` only. The ids are sequential ints, so any patient can read any other patient's glucose value and notes by counting up. **Fix:** `.Where(r => r.Id == id && r.PatientId == patient.Id)` and keep the 404. *rules/security.md (every lookup by ID checks ownership); auth-patterns §4*
2. **A01 cross-patient cache.** `Application/ReadingQueries.cs:32`. The cache key is the constant `"readings:latest"`. For one minute, every patient gets the newest reading of whoever called first, including `Notes`. **Fix:** key it per patient (`$"readings:v1:latest:{patient.Id}"`), or drop the cache. *caching (Rules: keys prefixed and by id)*
3. **A01 path traversal / arbitrary file write.** `Infrastructure/PhotoStore.cs:12`. `Path.Combine(folder, file.FileName)` takes a client-controlled name. `../../x` escapes the photo root. An absolute name like `/app/x` makes `Path.Combine` discard the root entirely. Either way the app user can overwrite any file it can write to. There is also no size limit and no content-type allowlist. **Fix:** a server-generated name (`$"{Guid.CreateVersion7()}.jpg"`), an allowlist of jpeg/png checked by magic bytes, `[RequestSizeLimit]` on the endpoint, and a check that `Path.GetFullPath(path)` starts with the root. *rules/security.md (validate file uploads, no path traversal); nginx (body size).* No skill covers uploads; that is a gap worth filling.
4. **A09 PHI and national ID in logs.** `Application/ReadingService.cs:25`. `{@Reading}` destructures `NationalId`, `Value`, `Notes` and `PhotoPath` into Elastic on every reading. `appsettings.json:14-17` sets up no masking. **Fix:** log `ReadingId` and `NeedsAlert` only, and add the destructuring policy plus the national-ID pattern operators. *pii-masking (Two layers, Setup)*
5. **A01/A08 national ID taken from the request body.** `Application/ReadingService.cs:7,14`, then sent to the care team at `ReadingNotifier.cs:19`. The caller chooses whose national ID goes on the alert. A malicious patient, or an app bug, sends nurses an alert about the wrong person, and the real patient's high reading never reaches their own team. **Fix:** take `NationalId` out of `NewReading`. Resolve it from the authenticated patient (a claim or the patient registry) when the alert is sent. *owasp-aspnetcore §5 (DTOs carry only fields the caller may set)*

### Warning
6. **A02/A10 internals in error responses.** `Api/ErrorMiddleware.cs:16` returns `ex.Message` and the inner exception's message. For example, PostgreSQL's `22001: value too long for type character varying(10)` comes back for an over-long national ID, and the `FormatException` from `decimal.Parse` echoes the value that was sent. Separately, `:14` logs `ex` unmasked. **Fix:** `AddProblemDetails` + an `IExceptionHandler` that returns a title and `traceId` only, and logs the exception type. *api-design §2; pii-masking (exceptions are not masked)*
7. **A07 JWT setup.** `Program.cs:25-29`:
   - `ClockSkew` is left at 5 min.
   - `MapInboundClaims` is left on.
   - The audience is hardcoded.
   - The authority is a Keycloak realm, whose `sub` is a UUID, yet `Api/CurrentPatient.cs:12` runs `int.Parse` on it. Unless the realm has a custom mapper, every request fails with a 500.

   **Fix:** follow auth-patterns §1 (30 s skew, `MapInboundClaims=false`, audience from config). Map `sub` to your int patient id through a table. Never parse it. *auth-patterns §1, §5*
8. **A01 no deny-by-default.** `Program.cs:30`: `AddAuthorization()` has no fallback policy, so the next endpoint added is open. **Fix:** `SetFallbackPolicy(RequireAuthenticatedUser)`. *auth-patterns §3*
9. **A06 no input validation.** Nothing checks `NewReading`: `Value` range, a `Unit` allowlist (a mg/dL value is compared against the mmol/L threshold of 11.1), `NationalId` format, `CollectedAt` (offset, not in the future), or `Notes` length (the `Notes` column is unbounded, `ReadingsDbContext.cs`, no `HasMaxLength`). In addition, `decimal.Parse` at `ReadingService.cs:14` uses the culture that `LanguageMiddleware.cs:14` set to `ar-SA`. As a result, a high reading can be rejected (alert lost) or misread, e.g. `11,5` becomes 115 under en-US. **Fix:** a FluentValidation validator, and parse with `InvariantCulture`. *rules/security.md Input Validation; api-design §3; localization §1.* The parsing detail is also the code reviewer's area.
10. **A10 the notifier fails open on availability and duplicates alerts.** `Application/ReadingNotifier.cs:15-36`:
    - Any care-team error escapes `ExecuteAsync`. The default `StopHost` then stops the whole API pod.
    - One alert the care team keeps rejecting blocks every alert queued behind it (it is first in the batch every time).
    - Each replica sends the same rows, because nothing claims them.
    - `Program.cs:21` (`AddStandardResilienceHandler`) retries POST to an endpoint with no idempotency key, which means repeat nurse alerts.

    **Fix:** catch, log and back off per alert. Claim rows with `FOR UPDATE SKIP LOCKED` or run a single replica. Add `options.Retry.DisableForUnsafeHttpMethods()`. *worker-patterns (polling worker); polly-resilience*
11. **A02 no security headers, HSTS or ForwardedHeaders.** `Program.cs:35-41`. *owasp-aspnetcore §1-2*
12. **A06 no per-caller rate limit** on `POST /readings` (each high value pages a nurse) or on the photo upload. *api-design §6*
13. **A02 credentials in source.** `appsettings.json:3` contains `Password=change-me`. **Fix:** remove it and inject from Vault or a K8s secret. *secret-management (appsettings holds defaults, no secrets)*
14. **A04/compliance PHI at rest and audit.** The national ID is stored in plaintext on every reading (`Reading.cs:26`), and photos are stored unencrypted. There is no PHI audit of reads or changes. **Fix:** stop storing the national ID (see 5), and add the PHI audit interceptor. *healthcare-compliance (PHI audit, Other safeguards); encryption-patterns*
15. **A07 outbound call has no service credentials.** `Infrastructure/CareTeamClient.cs:10` posts patient data with no token. Either the care-team API is open, or the calls will fail. **Fix:** client credentials. *auth-patterns §6*
16. **A03 supply chain unverified.** I could not run `dotnet list package --vulnerable --include-transitive` (no shell). The folder has no `Directory.Build.props`, so NuGetAudit is not turned into errors. Run the scan before merge; any hit blocks the merge. *zero-vulns*

### Suggestion
17. `ReadingEndpoints.cs:28` `.DisableAntiforgery()` is fine for a bearer-only API, since there are no cookies. The house rule's blanket "never bypass antiforgery" wording doesn't cover bearer APIs. Keep cookie auth off this service.
18. `ReadingQueries.cs:39` captures the first caller's `ct` inside the cache factory.

### Known-traps item `Program.cs:7` (scope validation off in Production)
**Dismissed as a security finding; it is outside my area.** The fact is right (Production skips `ValidateScopes`). From the security side, I checked for a scoped `ICurrentPatient` captured by a singleton, which would leak one patient's identity across requests. There is none: `ReadingNotifier` uses `IServiceScopeFactory`, and `PhotoStore` takes only `IConfiguration`. The code reviewer should rule on the DI hygiene itself.

**Not checked:** the vulnerable-package scan (no shell), tests, and the care-team API's own auth. No text in the repo addressed to an AI reviewer was found.

**Worth remembering for ECC:** proven-dotnet has no skill covering file-upload safety (client file names, path traversal, magic-byte checks). Only `rules/security.md` mentions it.

Files: /tmp/claude-0/labruns/with-proven-2/HomeReadings/{Program.cs, Api/ErrorMiddleware.cs, Api/CurrentPatient.cs, Api/ReadingEndpoints.cs, Application/ReadingQueries.cs, Application/ReadingService.cs, Application/ReadingNotifier.cs, Infrastructure/PhotoStore.cs, Infrastructure/CareTeamClient.cs, appsettings.json}