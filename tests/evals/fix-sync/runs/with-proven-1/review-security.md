## Security Review: MeterSync (/tmp/claude-0/syncruns/with-proven-1/MeterSync)
This was a read-only review; I changed no files. I found nothing in the repo addressed to an AI reviewer.

### Scan rows
| Row | Verdict |
|---|---|
| Program.cs:13 singleton `PatientDirectory` takes the DbContext | **Confirmed, critical.** It is resolved from the root, so every GET /meters shares one context. Concurrent requests throw "A second operation was started" and return 500. No cross-patient leak: the query filters by `patientId`. Fix: `AddScoped<PatientDirectory>()` (`dotnet-core`). |
| Program.cs:39 `SetIsOriginAllowed(_ => true)` + `AllowCredentials` | **Confirmed, critical.** Details in Critical 2. |
| AlertText.cs:13 time-zone lookup with no fallback | **Confirmed.** On an image without tzdata every alert throws. Fix: try `Asia/Riyadh`, else `CreateCustomTimeZone(..., +3h)`, cached in a static (`localization` §7). There is also a second problem on line 9: `new CultureInfo("ar-SA")` gives a Hijri date or a Gregorian one depending on the image's ICU, and with no ICU it throws `CultureNotFoundException` when the class first loads. Use `Cultures.ArabicGregorian`, or `InvariantCulture` with an explicit format (`localization` §1, §6). |
| Program.cs:30 `ValidateAudience = false` | **Confirmed. I'd raise it to High** (High 1). |
| appsettings.json:6 base address with no trailing `/` | **Confirmed.** Every vendor call goes to `/meters/...`, without the `/v2`. task.md fixes that value for production, so the fix belongs in code: add the trailing `/` in Program.cs:19 before `new Uri`. Validation that refuses the address would stop production from starting (`dotnet-core`). |
| Program.cs:9 scope validation | **Confirmed.** Set `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })`. This would have caught row 1 at startup. |

### Critical
1. **A01 Broken Access Control** `Api/MeterEndpoints.cs:24-49`. `LinkAsync` never looks at the caller. Any token can link any serial number to any national ID. The impact:
   - An attacker claims a victim's meter (they need only the serial number) and receives the victim's glucose readings.
   - An attacker links their own meter to a victim and sends the victim false alert SMS.
   - The 404 / 409 / 201 answers tell anyone whether a 10-digit national ID belongs to a registered patient.

   **Fix:** load the patient by `CurrentPatient.Id(user)`. Return `NotFound` unless `patient.NationalId == request.NationalId`. This keeps the body shape (`auth-patterns` §4, rules/security.md "every lookup checks ownership").
2. **A01 CORS** `Program.cs:38-42`. Any site gets `Access-Control-Allow-Origin` with its own origin, plus `Allow-Credentials: true`. **Fix:** `p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()!).WithMethods("GET","POST").WithHeaders("Authorization","Content-Type")`, with no `AllowCredentials`, because this is a bearer-token API (`owasp-aspnetcore` §3).

### High
1. **A07** `Program.cs:25-33`. Any token from the `patients` realm is accepted, whichever client it was issued for. **Fix:** `o.Audience = "meter-sync"; o.MapInboundClaims = false; ValidateAudience = true, ValidateIssuer = true, ValidateLifetime = true` (`auth-patterns` §2, §8). `CurrentPatient` already falls back to reading `sub`, so it keeps working.
2. **A01/A05 path change through the serial number** `Infrastructure/MeterVendorClient.cs:11` and `MeterEndpoints.cs:27`. `Uri.EscapeDataString` leaves `.` and `..` as they are, so the serial `..` becomes `meters/../readings`, which resolves to a different vendor path. Its response would be stored as that patient's readings. **Fix:** check the serial against an allowlist in `LinkAsync`, e.g. `^[A-Za-z0-9-]{1,40}$`, and return a 400 otherwise. Validate `NationalId` too: `^[12]\d{9}$`.

### Medium
1. **A02/A10** `Program.cs:44-49`. There is no `AddProblemDetails()` / `UseExceptionHandler()`, so a Production 500 has no body and no trace id. Two pods can both pass the check at `MeterEndpoints.cs:41` and race to insert the same serial. The unique index then throws `DbUpdateException`, which surfaces as a 500. **Fix:** add the exception handler, and catch the unique violation (`PostgresException.SqlState == "23505"`) and map it to 409 (`api-design` §2).
2. **A10** `Api/CurrentPatient.cs:9`. If `sub` is missing or not a number, `int.Parse` throws and the caller gets a 500. It fails closed, but **fix** it with `int.TryParse` and return 401 or 404 (`auth-patterns` §5).
3. **A02** `Program.cs`. There are no security headers and no HSTS. **Fix:** headers in `OnStarting`, HSTS for 365 days, and `UseForwardedHeaders` behind nginx (`owasp-aspnetcore` §1-2).
4. **A06** POST /meters has no per-caller rate limit, and it was a national-ID lookup until Critical 1 is fixed. **Fix:** a partitioned limiter keyed on `sub` (`api-design` §6).
5. **A03** There is no `Directory.Build.props` setting `NuGetAudit`, `NuGetAuditMode=all` and NU1901-NU1904 as errors. All packages are pinned at 10.0.0. **I could not run `dotnet list package --vulnerable --include-transitive` here.** It has to show zero hits before release, and any hit blocks the release (`zero-vulns`).

### Low
1. **A09** `MeterSyncService.cs:23` writes `HI`/`LO` values (health data) to the log next to the vendor id. Log the vendor id only (`pii-masking`). The default HttpClient logging writes the vendor URL, which contains the meter serial (a device identifier). `SyncWorker.cs:23` passes the whole exception to `LogError`; the current exceptions don't appear to carry phone numbers, but log the type and status code (`pii-masking`, "Neither layer touches the exception").
2. Vendor responses are read into memory with no size cap (`MeterVendorClient.cs:11`): set `MaxResponseContentBufferSize`. Leave `SmsClient` without a retry handler: retrying a POST sends a duplicate SMS (VENDOR.md:19).
3. The SMS carries the glucose value (PHI) to the phone. That is the stated design; it is for compliance-auditor to sign off.

### Outside security, but they block production
These are patient-safety and two-pod problems; pass them to code-reviewer.
- `MeterSyncService.cs:21`: `HI`/`LO` readings are skipped, so the most dangerous values (over 600 or under 20 mg/dL) never trigger an alert.
- `MeterSyncService.cs:27`: mmol/L values are stored as mg/dL without multiplying by 18. Every mmol/L reading, high ones included, is flagged as Low.
- `AlertSweeper.cs:16-31`: both pods pick up the same due readings and send the SMS twice; the gateway has no idempotency key. Fix: claim each row with a conditional `ExecuteUpdate` or `FOR UPDATE SKIP LOCKED` before sending.
- `MeterSyncService.cs:32`: both pods insert the same `VendorId`, the unique index throws, and that pod's whole batch is lost.

### Not checked
- The vulnerable-package scan (I could not run it here).
- Runtime behaviour: I ran no build or tests; this review comes from reading the code.

Skills cited: `owasp-aspnetcore`, `auth-patterns`, `pii-masking`, `localization`, `dotnet-core`, `api-design`, `zero-vulns`.