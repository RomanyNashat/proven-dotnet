## Security Review: MeterSync (/tmp/claude-0/syncruns/with-proven-2/MeterSync)

I read every source file, task.md, scan.md and VENDOR.md. I did not build or run anything (this agent has no shell), so the package vulnerability check was not done. No repo text tried to instruct an AI reviewer.

### Scan rows
1. `Program.cs:13`, singleton `PatientDirectory` holding a DbContext: **confirmed, critical.** Every `GET /meters` on a pod shares one context that is never disposed. Concurrent requests throw "A second operation was started on this context" and return 500s. Fix: `AddScoped<PatientDirectory>()` (`dotnet-core`, host section).
2. `Program.cs:39`, `SetIsOriginAllowed(_ => true)` with `AllowCredentials`: **confirmed, but High, not critical.** The API uses bearer tokens only and no cookies, so a browser on another site can't attach the token today. It is still the open-echo hole, and it becomes exploitable as soon as any cookie auth is added. Fix: `.WithOrigins(config.GetSection("Cors:Origins").Get<string[]>())`, explicit methods and headers, no `AllowCredentials` (`owasp-aspnetcore` §3).
3. `AlertText.cs:13`, time-zone lookup with no fallback: **confirmed.** Line 9 is worse. Without ICU, `new CultureInfo("ar-SA")` throws `CultureNotFoundException` inside a static initializer, so every call to `AlertText.For` fails with `TypeInitializationException`. With some ICU versions, `ar-SA` prints the date in Hijri. Fix: the fixed-offset `RiyadhTime` fallback (`localization` §7), and a named Gregorian Arabic culture or invariant digits instead of plain `ar-SA` (`localization` §6, table lines 23-26).
4. `Program.cs:30`, `ValidateAudience = false`: **confirmed, raised to High.** `sub` becomes the patient id directly (`CurrentPatient.cs:9`), so a token issued to any other client in the `patients` realm acts as that patient here. Fix: `o.Audience = "meter-sync"`, `ValidateAudience = true` (`auth-patterns` lines 45-51 and 122-124).
5. `appsettings.json:6`, base address without a trailing slash: **confirmed.** `meters/{sn}/readings` resolves to `https://meters.example.net/meters/...` and drops `/v2`. Production keeps this value (task.md:11), so fix it in code: append `/` when it's missing at `Program.cs:19`, or validate options (`dotnet-core` lines 59-84).
6. `Program.cs:9`, scope validation off in Production: **confirmed, info.** Add `builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; })`. That would have refused to start with row 1 in place.

### New findings: Critical
1. **A01 IDOR** in `Api/MeterEndpoints.cs:35`. `POST /meters` links a meter to whichever patient owns `request.NationalId` and never looks at the caller. Any token holder can attach a meter to any patient, which sends them false glucose alerts and puts foreign readings in their record. The 404 vs 201/409 difference also tells an attacker whether a national ID is registered. Fix, keeping the body shape: add `ClaimsPrincipal user`, load `db.Patients.SingleOrDefaultAsync(p => p.Id == CurrentPatient.Id(user))`, and return `NotFound()` when it's null or its `NationalId != request.NationalId` (`auth-patterns`: someone else's record is a 404; `rules/security.md` ownership rule).

### High
2. **A10 one bad reading stops all alerts** in `AlertSweeper.cs:28`. `phones[reading.PatientId]` throws `KeyNotFoundException` when a reading has no patient (there is no FK on `PatientId`, `SyncDbContext.cs:24-45`). An SMS 4xx or the `AlertText` failure from row 3 does the same. The batch aborts, and the next run picks the same first 100 rows by `Id`, so no patient is ever texted again. Fix: catch per reading, record a failure (attempt count or failed-at column) so the sweep moves past it, and add FK constraints.
3. **Duplicate SMS across pods** in `AlertSweeper.cs:16-30`. Both pods select the same unsent readings, and the gateway has no idempotency key (`VENDOR.md:19`). A send followed by a failed `SaveChanges` also resends. Fix: claim rows with `FOR UPDATE SKIP LOCKED` (`worker-patterns` lines 88-119), and decide on send-once ordering explicitly.
4. **A06 meter takeover by serial number.** Nothing proves the caller holds the meter: whoever links a serial first gets that meter's readings. I can't fix this within the fixed request shape. Raise it with product or the vendor (a pairing code).

### Medium
5. **A07** `CurrentPatient.cs:9`: `int.Parse` on a missing or non-numeric `sub` throws and returns 500. Use `TryParse` and return 401/403. Consider `MapInboundClaims = false` and reading `sub` directly (`auth-patterns` lines 126 and 283).
6. **A02** `Program.cs:44-49`: no `AddProblemDetails`/`UseExceptionHandler`, no security headers, no HSTS. `GET /meters` returns patient-linked data with no `Cache-Control: no-store`. Fix: `owasp-aspnetcore` §1 (headers set in `OnStarting`) and §2 (HSTS for 365 days, with ForwardedHeaders behind nginx).
7. **A09 health data in logs**:
   - `MeterSyncService.cs:23` logs `Value`, and `HI`/`LO` mean glucose above 600 or below 20. Log the `VendorId` and a reason only.
   - The default HttpClient logging writes the vendor URI, which contains the serial number, and the exception text (`pii-masking` lines 105-110). Call `.RemoveAllLoggers()` on both clients, or raise the `System.Net.Http.HttpClient` log level to Warning.
8. **A06** no per-caller rate limit on `POST /meters`. Add a partitioned limiter keyed on `sub` (`api-design` §6).

### Low
9. `MeterEndpoints.cs:27-35`: `NationalId` isn't validated (null, length, digits). Require 10 digits.
10. `MeterEndpoints.cs:41-48`: `AnyAsync` then save races across pods, so the unique index returns a 500 instead of a 409. Catch `PostgresException` with SqlState `23505` and return `Conflict()`.

### Outside security, but patient safety
- `MeterSyncService.cs:21-27`: `HI`/`LO` readings are skipped, so the most dangerous readings never alert.
- `mmol/L` values aren't converted (×18, `VENDOR.md:14`). A reading of 15 mmol/L (270 mg/dL) is stored as 15 and alerts as low.
- A single `SaveChanges` at line 32 means one failed vendor call loses the whole run's progress.

### Not checked
- `dotnet list package --vulnerable --include-transitive` was not run. Every package is pinned at `10.0.0` (`MeterSync.csproj:9-11`), which may have later patches. The orchestrator should run it, and under zero-vulns any hit blocks release.
- I saw no secrets in `appsettings.json`; the connection string has no password.

Suggested memory: none.