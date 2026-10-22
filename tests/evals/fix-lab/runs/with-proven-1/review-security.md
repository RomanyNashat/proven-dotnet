## Security review: HomeReadings (/tmp/claude-0/labruns/with-proven-1/HomeReadings)

The service is not ready for production. Five findings are Critical: three leak one patient's data to another, one lets a caller write files anywhere on the pod, and one lets a patient send an alert under someone else's national ID. Skills cited are under /home/claude/proven-dotnet/.claude/skills/.

### Critical
1. **A01 IDOR**, `Application/ReadingQueries.cs:12-16`, reached by `Api/ReadingEndpoints.cs:17`. `GetAsync` filters only by `r.Id == id`. The ids are sequential `int`s, so any signed-in patient can read every other patient's glucose values and notes by counting up. **Fix:** `.Where(r => r.Id == id && r.PatientId == patient.Id)`; another patient's record then returns 404. (`auth-patterns` §4, `rules/security.md` "Every lookup by ID checks ownership")
2. **A01 cross-patient cache leak**, `Application/ReadingQueries.cs:32`. The cache key `"readings:latest"` is shared by everyone. The first patient's latest reading is served to every patient on that pod for a minute. This endpoint runs on every app start, so it will happen. **Fix:** read `var id = patient.Id;` outside the factory and use the key `$"readings:v1:latest:{id}"`. (`caching` Rules: keys are stable, prefixed and versioned)
3. **A01/A05 path traversal, arbitrary file write**, `Infrastructure/PhotoStore.cs:12`. `Path.Combine(folder, file.FileName)` uses the file name the client sent. `../../..` climbs out of the photo folder. A rooted name such as `/app/HomeReadings.dll` makes `Path.Combine` drop the root entirely, so the caller can overwrite the app's binaries (possible code execution) or another reading's photo. The endpoint (`Api/ReadingEndpoints.cs:26-28`) also has no size limit and no content-type check. **Fix:**
   - Save under a name the server makes: `$"{Guid.CreateVersion7():N}{ext}"`, with `ext` taken from an allowlist (`.jpg`, `.png`) after checking the file's magic bytes.
   - Check `Path.GetFullPath(path).StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar)`.
   - Add `.WithMetadata(new RequestSizeLimitAttribute(5_000_000))`.
   (`rules/security.md` Input Validation: size limits, content-type allowlist, no path traversal)
4. **A01/A08 identity taken from the body**, `Application/ReadingService.cs:7` and `:14`. `NationalId` comes from the request, not from the signed-in patient. A patient can file a reading, and so a care-team alert (`ReadingNotifier.cs:19`), under any national ID. A nurse then acts on the wrong person. **Fix:** remove `NationalId` from `NewReading`. Look it up server-side from `patient.Id` (patient table or a token claim). (`owasp-aspnetcore` §5: DTOs carry only the fields a caller may set)
5. **A09 PHI in logs**, `Application/ReadingService.cs:25`. `{@Reading}` writes the national ID, glucose value, notes and photo path to Elastic on every reading. `Program.cs:9` and `appsettings.json:14-17` set up no destructuring policy and no masking operators. **Fix:** log only `{ReadingId}` and `{NeedsAlert}`. Add `SensitiveFieldDestructuringPolicy` and the Saudi national ID operator. (`pii-masking` "Two layers", Setup)

### Warning
6. **A02/A10 internals in error responses**, `Api/ErrorMiddleware.cs:16`. `ex.Message` and the inner exception's message go back to the client as raw JSON, not ProblemDetails, with no trace id. Since .NET 8, a `FormatException` repeats the bad input: the meter text from `decimal.Parse`, or the user's `sub` from `int.Parse`. Npgsql messages name tables, columns and constraints. Line `:14` also writes the unmasked exception to the log. **Fix:** replace this middleware with `UnexpectedExceptionHandler`, `AddProblemDetails` and `UseExceptionHandler`. (`api-design` §2, `pii-masking` "Neither layer touches the exception")
7. **A07 claim mapping**, `Api/CurrentPatient.cs:12` and `Program.cs:25-29`.
   - `int.Parse` runs on `NameIdentifier`, which is `sub` mapped by default. The authority is Keycloak (`appsettings.json:6`), and Keycloak's `sub` is a UUID. If so, every authenticated call is a 500 that echoes the `sub` (via #6). It fails closed, but it is broken. **Fix:** set `MapInboundClaims = false`, map `sub` to the `int` patient id through a table or a dedicated claim, and use `TryParse`, answering 401 on failure.
   - `ClockSkew` is left at 5 minutes. **Fix:** set it to 30 s.
   - The audience is hard-coded. **Fix:** read it from configuration. (`auth-patterns` §1, §5, §8)
8. **A01 no fallback policy**, `Program.cs:30`. Only the `/readings` group requires auth, so any endpoint added later is anonymous. **Fix:** `AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())`. (`auth-patterns` §3)
9. **A05/A06 no input validation**, `Application/ReadingService.cs:7,14`.
   - Nothing validates `NewReading`. A national ID longer than its 10-character column fails in the database, not as a 400. `Notes` has no `HasMaxLength` (`Infrastructure/ReadingsDbContext.cs:13-24`), so its column is unbounded. `CollectedAt` is accepted as sent, future dates and missing offsets included.
   - `decimal.Parse(request.Value)` uses the request culture, `ar-SA` or `en-US` (`Api/LanguageMiddleware.cs:13-15`). Under `en-US`, `"12,5"` parses as 125, which raises a false alert. Under `ar-SA` the decimal separator can be `٫`, so a meter's `12.5` can fail to parse. The alert decision depends on this value.
   - **Fix:** a FluentValidation validator with lengths matching the columns; `decimal.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, …)` after normalising Arabic-Indic digits, answering 400 on failure. (`api-design` §3, `localization`, `rules/csharp-standards.md`)
10. **A10/A08 care-team alerts**, `Program.cs:21` and `Application/ReadingNotifier.cs:16-21,35`.
    - `AddStandardResilienceHandler` retries POST, and the care team's system has no idempotency key, so a timed-out send can page a nurse twice. **Fix:** `DisableForUnsafeHttpMethods()` (`polly-resilience`).
    - An exception escaping `RunOnceAsync` stops the host (the default `BackgroundServiceExceptionBehavior.StopHost`). A care-team outage then takes the patient API down. **Fix:** catch each tick, log the exception type, and carry on.
    - The send happens before the save, and every replica polls the same rows. Both cause re-sends. Detail is for the code reviewer.
11. **A03 supply chain not enforced**, `HomeReadings.csproj`. There is no `Directory.Build.props` with `NuGetAuditMode=all` and NU1901-1904 treated as errors. I could not run `dotnet list package --vulnerable --include-transitive`, so the zero-vulns status is **unverified, not passed**. (`zero-vulns`)
12. **A02 default credential in source**, `appsettings.json:3`. `Password=change-me` is committed and works whenever the environment override is missing. **Fix:** remove it; the value comes from a K8s Secret or the vault, checked with `ValidateOnStart`. (`secret-management`, `rules/security.md` Secrets)
13. **A02 no security headers, HSTS or `Cache-Control: no-store`** on responses carrying PHI, `Program.cs:35-41`. **Fix:** `UseApiSecurityHeaders()` registered first, and agree with SRE whether HSTS is set at nginx. (`owasp-aspnetcore` §1-2)
14. **A06 no per-caller rate limits** on POST `/readings` or on photo uploads. (`api-design` §6)
15. **A09 no PHI audit trail** for creating, reading or attaching photos to readings. (`healthcare-compliance` "The PHI audit")

### Suggestion / checked and fine
- **Known-traps `Program.cs:7` (ValidateScopes/ValidateOnBuild): confirmed as info.** It is mainly the code reviewer's area. No singleton holds a scoped service today: `ReadingNotifier` uses the scope factory, and `PhotoStore` takes only `IConfiguration`. But tests run in Production, so a future captive DbContext would pass them unnoticed. Turn both checks on. (`dotnet-core`)
- The ownership check in `AttachPhotoAsync` (`ReadingService.cs:32`) is correct and returns 404.
- `.DisableAntiforgery()` is acceptable because authentication is bearer-only, with no cookies.
- The anonymous health endpoints return only status text.
- No raw SQL, no `BinaryFormatter`, no SSRF route: the care-team base URL comes from configuration.

**Not checked:** the vulnerable-package scan (no tool run), the token settings on the Keycloak side (audience mapper, `sub` format), nginx and ingress configuration, and any test code.