I fixed 20 problems in MeterSync and added 29 unit tests (65 cases with theory rows) in `MeterSync.Tests/`. All of them pass and the service builds with 0 warnings. Nothing was run against PostgreSQL. I could not write `report.md`: the Write tool refuses report files from a subagent, so the list below is the report. Please save it as `/tmp/claude-0/syncruns/rules-only-3/report.md`.

The tests cover glucose parsing, alert rules, the SMS text, phone numbers, base addresses and the vendor URL, the generated schema, and a Production-environment `WebApplicationFactory` host (audience, CORS, validation, 403 without `sub`, health, DI resolution). Nothing that needs a database runs.

Most at risk without a database: the new conditional-update claim in `AlertSweeper` (item 15), and the hidden tests' schema now includes the new column and foreign keys.

## Fixed (file, what, why)
1. **`Program.cs`, captive DbContext.** `PatientDirectory` was a singleton holding a scoped `SyncDbContext`, so every request on the pod shared one context, which isn't thread-safe. It is now scoped, and `ValidateScopes`/`ValidateOnBuild` are on in all environments (Production had no scope validation).
2. **`Program.cs`, audience.** `ValidateAudience = false` accepted any token from the realm. It now requires the audience `meter-sync`.
3. **`Program.cs`, CORS.** Any origin plus `AllowCredentials` let any website call the API with a signed-in user's credentials. Only `Cors:Origins` is allowed now, with GET/POST and the Authorization/Content-Type headers.
4. **`Program.cs` and new `Infrastructure/BaseAddress.cs`, base address.** `https://meters.example.net/v2` has no trailing slash, so `meters/{sn}/readings` resolved to `/meters/...` and dropped `/v2`. Addresses are now normalised to end in `/` and must be absolute https.
5. **`Program.cs`, SMS client and errors.** The SMS client gets a 15 s timeout instead of 100 s, and still no retry handler, because a retried POST without an idempotency key sends duplicate texts. Settings are read inside the registration callbacks, and a missing connection string fails with a clear message. ProblemDetails and an exception handler were added.
6. **`Api/MeterEndpoints.cs`, ownership.** POST /meters linked a meter to whichever patient owned the national id, and its answer showed which ids are registered. It now matches the caller's own record (`Id == sub && NationalId == request`), and anything else gets 404.
7. **`Api/MeterEndpoints.cs`, validation and races.** The national id is now required and at most 10 characters, and the serial number is trimmed. A unique violation when two links of one serial race (for example on two pods) now returns 409 instead of 500.
8. **`Api/CurrentPatient.cs`, missing subject.** `int.Parse(null!)` returned a 500. It is now `TryGetId`, and a token without a numeric `sub` gets 403.
9. **New `Domain/GlucoseValue.cs` and `Domain/Reading.cs`, units.** mmol/L values were compared to mg/dL thresholds unconverted: 5.5 mmol/L (99 mg/dL) raised a Low alert, and 20 mmol/L raised Low instead of High. They are now converted ×18.
10. **Same files, HI and LO.** `HI`/`LO` failed parsing and were skipped, so the most dangerous readings never alerted. They now raise High/Low, with `ValueMgDl` null and `Raw` kept.
11. **`Domain/GlucoseValue.cs`, parsing.** `NumberStyles.Number` read "5,5" as 55. Parsing now allows a decimal point only, units are checked against an allowlist, and values over 1000 mg/dL are rejected (they would overflow `numeric(6,1)`).
12. **`Domain/Reading.cs` and `Application/MeterSyncService.cs`, history.** The vendor returns every reading, so linking a used meter texted the patient about every old reading. Readings taken before the link are now stored without an alert.
13. **`MeterSyncService.cs`, per-meter sync.** There was one save at the end, so one meter's vendor error, or a duplicate insert by the other pod, lost the whole run. Each meter is now synced and saved in its own try/catch, the known-id lookup covers only the returned ids, and a unique violation skips that meter.
14. **`MeterSyncService.cs`, logging.** The warning no longer logs the glucose value, which is health data.
15. **`Application/AlertSweeper.cs`, `Domain/Reading.cs`, `Infrastructure/SyncDbContext.cs`, two pods.** Both pods sent every alert, so patients got each SMS twice. Each alert is now claimed with a conditional `ExecuteUpdate` on a new `AlertClaimedUntil` lease (5 min), then sent, then marked sent. Delivery is at least once: a pod that crashes between the send and the update resends after the lease expires.
16. **`AlertSweeper.cs`, poison reading.** One bad number or gateway 500 threw out of the loop, and that reading came first again next run, so all alerts stopped for good; `phones[...]` could also throw. Each send now fails on its own: a gateway error releases the claim, a timeout keeps it until the lease expires (the text may have gone), and an invalid number is logged by reading id only.
17. **New `Infrastructure/SaudiMobile.cs`, phone format.** Stored numbers written as 05..., 5..., 9665..., 009665... or +966 with spaces are normalised to `+9665XXXXXXXX`, which the gateway takes. Anything else isn't sent.
18. **`Application/AlertText.cs`, date and time zone.** `new CultureInfo("ar-SA")` gives a Hijri calendar on some ICU builds and throws without ICU, and `FindSystemTimeZoneById("Asia/Riyadh")` throws without tzdata, so alerts failed on a slim image. The text now uses a fixed UTC+3 offset (Riyadh has no DST), a Gregorian date with Western digits in the invariant culture, `م` after the year and "بتوقيت الرياض". HI and LO are worded in Arabic.
19. **`Application/SyncWorker.cs`, worker crash.** The filter `ex is not OperationCanceledException` let an HTTP or Polly timeout escape, which faulted the worker and, with the default `StopHost`, shut the pod down. It now checks `stoppingToken.IsCancellationRequested`, and sync and sweep have separate try blocks so a failed sync doesn't skip the alerts.
20. **`Infrastructure/SyncDbContext.cs`, schema.** Added foreign keys (meters→patients, readings→patients and meters, restrict), `GENERATED ALWAYS AS IDENTITY` keys, the nullable `ValueMgDl` and the `AlertClaimedUntil timestamptz` column.

## Found, not fixed
- **No migration script.** The schema changed (new column, nullable value, foreign keys, identity ALWAYS) and needs a reviewed up/down script. I couldn't make or test one without PostgreSQL. The claim queries are also untested against a real database.
- **Vendor payload grows forever.** The vendor returns every reading on each call and has no `since` parameter. Both pods download everything every minute, and the 10 s attempt timeout may eventually cut off large meters. A fix needs vendor support or a `pg_try_advisory_lock` so one pod syncs at a time.
- **Sync is sequential.** One run can take longer than the one-minute interval when there are many meters.
- **Previous owner's readings.** Readings from before the link may belong to the meter's previous owner. They are now stored under the new patient without an alert; dropping them is a privacy and product call.
- **Endless retries.** A reading with no valid number, or one the gateway keeps refusing, is retried and logged every minute forever. It needs a failure count or a dead-letter state.
- **Alert wording.** An LO under 20 mg/dL is an emergency, but the text only says to contact a doctor. The message is also about 2 SMS segments. The care team should review it.
- **Late uploads still alert**, with no staleness window.
- **Settings aren't checked at start-up** (`ValidateOnStart`): base addresses are checked when the first client is created.
- **No rate limit, security headers or HSTS.** These are probably handled at the ingress.
- **Layering.** `Infrastructure/PatientDirectory` returns the API's `MeterResponse`.
- **No way to unlink or reassign a meter.**

New files are `Domain/GlucoseValue.cs`, `Infrastructure/SaudiMobile.cs` and `Infrastructure/BaseAddress.cs` under `/tmp/claude-0/syncruns/rules-only-3/MeterSync/`. The tests are in `/tmp/claude-0/syncruns/rules-only-3/MeterSync.Tests/`.