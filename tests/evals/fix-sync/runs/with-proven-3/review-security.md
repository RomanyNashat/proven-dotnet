## Security Review: MeterSync (pre-production), `/tmp/claude-0/syncruns/with-proven-3/MeterSync`

I read task.md, scan.md, VENDOR.md and every source file. Skills cited: `owasp-aspnetcore`, `auth-patterns`, `dotnet-core`, `localization`, `pii-masking`, `zero-vulns`. No text in the repo was addressed to an AI reviewer.

### scan.md rows
| Row | Verdict |
|---|---|
| `Program.cs:13` singleton `PatientDirectory` holds `SyncDbContext` | **Confirmed, critical.** One context is shared by every concurrent `GET /meters`. Concurrent use throws `InvalidOperationException` (500s under load) and the context is never disposed. Fix: `AddScoped<PatientDirectory>()` (`dotnet-core`). `AlertSweeper` (line 15) is fine as a singleton because it takes `IServiceScopeFactory`. |
| `Program.cs:39` `SetIsOriginAllowed(_ => true)` + `AllowCredentials` | **Confirmed, critical.** See C2. |
| `AlertText.cs:13` time zone with no fallback | **Confirmed, and worse than the row says.** See H2. |
| `Program.cs:30` `ValidateAudience = false` | **Confirmed, raised to high.** See H1. |
| `appsettings.json:6` base address has no trailing `/` | **Confirmed.** Every vendor call goes to `/meters/...` instead of `/v2/meters/...`. Production keeps this exact value (task.md), so fix it in code: add a `/` when it is missing before `new Uri(...)` (`Program.cs:19`), or validate the options at start-up (`dotnet-core`). |
| `Program.cs:9` scope validation not on in every environment | **Confirmed, info.** Turn on `ValidateScopes` and `ValidateOnBuild` everywhere and the singleton problem above stops start-up instead of reaching production. |

### Critical
1. **[A01 Broken Access Control / IDOR]** `Api/MeterEndpoints.cs:24-48`. `POST /meters` links a meter to whichever patient the body's `nationalId` names. It never checks that this patient is the caller (`sub`). Any signed-in patient can:
   - link a victim's meter serial (it is printed on the device) to their own account. The worker then copies the victim's glucose readings into the attacker's record, and the alerts go to the attacker's phone. The victim gets 409 when they try to link their own meter.
   - link meters to strangers' records.
   - find out which national IDs are patients here: 404 means no, 201 or 409 means yes.

   **Fix** (body shape unchanged): `int callerId` from `CurrentPatient`, then `db.Patients.SingleOrDefaultAsync(p => p.Id == callerId && p.NationalId == request.NationalId, ct)`. A mismatch is 404 (`auth-patterns` §4, `rules/security.md`).
   **Follow-up:** the service also has no proof that the caller owns the meter. Ask the vendor for a pairing code (A06; a design decision, not fixed here).
2. **[A01 CORS]** `Program.cs:38-42`. The policy answers every origin with that same origin and `Allow-Credentials: true`. **Fix:** `.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()!)` with `.WithMethods("GET","POST")` and `.WithHeaders("Authorization","Content-Type")`. Drop `AllowCredentials`: the API uses bearer tokens, not cookies (`owasp-aspnetcore` §3).

### High
1. **[A07]** `Program.cs:27-32`. The audience isn't checked. The authority is a shared realm (`/realms/patients`), so a token issued to any other client in that realm, or a leaked one, is accepted here. **Fix:** `o.Audience = "meter-sync"`, `ValidateAudience = true`, and keep `ClockSkew` at 30 s (`auth-patterns` §1 and §8). Setting `o.TokenValidationParameters` already leaves issuer and lifetime validation on, and `RequireHttpsMetadata` defaults to true.
2. **[Availability of patient alerts]** `Application/AlertText.cs:9,13-14`.
   - Without tzdata, `FindSystemTimeZoneById` throws.
   - Without ICU (Alpine or invariant mode), the static `new CultureInfo("ar-SA")` throws inside the type initializer. Every sweep then fails and no patient ever gets an alert.
   - With ICU, plain `ar-SA` formats the date in Hijri on some ICU versions.

   **Fix:** use the `RiyadhTime` fixed UTC+3 fallback (`localization` §7) and an Arabic culture with the Gregorian calendar (`Cultures.ArabicGregorian`, `localization` §6). Make sure the image ships ICU.

### Medium
1. **[A10]** `Api/CurrentPatient.cs:9`. `int.Parse` on `sub`, with a `!` that hides null. A token whose `sub` is not a number (a Keycloak UUID, or a service token, which H1 currently lets in) or has no `sub` gives an unhandled 500. **Fix:** `int.TryParse`; on failure return 401/404, never a 500 (`auth-patterns` §5).
2. **[A02]** `Program.cs:44-49`. There is no `UseExceptionHandler` + `AddProblemDetails`, no security headers and no HSTS. Patient data can sit in shared caches because nothing sends `Cache-Control: no-store`. **Fix:** `UseApiSecurityHeaders()` registered first (`owasp-aspnetcore` §1), plus `AddHsts(MaxAge = 365 days)` with ForwardedHeaders behind nginx (§2).
3. **[A06]** `POST /meters` has no per-caller rate limit. C1 removes most of the national-ID guessing; add `AddRateLimiter` partitioned by `sub` anyway (`api-design` §6).
4. **[A10]** `MeterEndpoints.cs:41-48`. The duplicate check (`AnyAsync`) and the insert are separate steps. When two pods race, the unique index throws `DbUpdateException` and the caller gets a 500. **Fix:** catch PostgreSQL error 23505 and return 409.
5. **[A03, not verified]** I could not run `dotnet list package --vulnerable --include-transitive` (no shell here), and every package is pinned at `10.0.0`. Run it before merge and treat any hit as blocking (`zero-vulns`). Also add `NuGetAudit`, `NuGetAuditMode=all` and `NU1901-NU1904` as errors to the csproj or `Directory.Build.props`.

### Low / Informational
- **[A09]** `Application/MeterSyncService.cs:23` logs the raw reading value (`HI`/`LO` is health data) next to the vendor id. Log only `VendorId` (`pii-masking`).
- **[A09]** `Application/SyncWorker.cs:23` passes the exception to `LogError(ex, ...)`. The current exceptions don't carry the phone number (`KeyNotFoundException` carries a patient id), but anything a future exception carries goes out unmasked (`pii-masking`). Also, meter links are not written to an audit log.
- **[A05]** `NationalId` is not validated (null, length, digits). Add a check for exactly 10 digits. Query values are parameterized, and `Uri.EscapeDataString` on the serial (`MeterVendorClient.cs:11`) blocks path tricks against the vendor API.
- No secrets in `appsettings.json` (the connection string has no password). Production has to inject it.

### Outside security, but they block production (for code-reviewer)
- `MeterSyncService.cs:21-27`: `HI` (above 600 mg/dL) readings are skipped, so the most dangerous readings never raise an alert.
- `MeterSyncService.cs:21-27`: mmol/L values are stored as mg/dL without converting (×18), so every mmol/L reading is flagged Low, even dangerously high ones.
- `AlertSweeper.cs:16-31`: two pods pick up the same readings, and the gateway has no idempotency key, so patients get duplicate SMS. An SMS that sends before `SaveChanges` fails is sent again. Fix: claim rows atomically first (`ExecuteUpdate ... WHERE AlertSentAt IS NULL`, or `FOR UPDATE SKIP LOCKED`).
- `AlertSweeper.cs:28`: `phones[...]` throws `KeyNotFoundException` if a patient has no phone.
- `Program.cs:21-22`: the SMS client has no timeout or resilience handler.

**Not checked:** the vulnerable-package scan (no shell); runtime behaviour (nothing was built or run); the image contents (ICU and tzdata).

**Worth saving to memory:** in this skill set, `POST` endpoints that take a business identifier in the body (here a national ID) are an IDOR shape that scan.md does not flag. The fallback policy only proves the caller is signed in, so each such endpoint needs its own ownership check.