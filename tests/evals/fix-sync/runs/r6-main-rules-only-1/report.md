# MeterSync review

The service builds with 0 warnings. 39 unit and host tests in `MeterSync.Tests/` pass. Nothing that needs
PostgreSQL ran: the two-pod claim, the unique-violation handling and the foreign keys are checked by
generating SQL only.

## Fixed

### Patient safety (alerts)
| File | What | Why |
|---|---|---|
| `Domain/Reading.cs`, `Application/MeterSyncService.cs` | `HI` and `LO` readings are now stored and raise High and Low alerts. `ValueMgDl` is `null` for them. | These failed `decimal.TryParse` and were skipped with a warning. That means a glucose over 600 or under 20 mg/dL, the most dangerous readings, never sent an SMS. |
| `Domain/Reading.cs` | `mmol/L` values are converted to mg/dL (×18) before they're compared with the thresholds. Unknown units are rejected. | 15 mmol/L (270 mg/dL, high) was stored as 15 "mg/dL" and texted as **low**. Every mmol/L meter got wrong alerts. |
| `Domain/Reading.cs` | Parsing uses `NumberStyles.AllowDecimalPoint`, not `Number`. Non-positive and implausible values are rejected. Values are rounded to the column's 1 decimal place before they're classified. | `Number` allows thousands separators, so `"5,6"` parsed as 56. |
| `Application/AlertSweeper.cs`, `Domain/Reading.cs` (`AlertClaimedUntil`) | Each alert is claimed with one conditional `UPDATE` before it's sent. The claim is a 5-minute lease and is released if the send fails. The sweeper only marks alerts it sent, and returns the number this call sent. | Every pod ran the sweeper over the same unsent rows. With no idempotency key at the gateway, each patient got one SMS per pod. |
| `Application/AlertSweeper.cs` | Failures are handled per message: log, release the claim, move on. "Sent" is written with `CancellationToken.None`. | Before, one failed SMS threw out of the loop and stopped the rest of the batch. A shutdown between the send and the save also caused a resend. |
| `Domain/PhoneNumber.cs`, `AlertSweeper.cs` | Phone numbers are normalised to E.164 (`05…`, `9665…`, `009665…` → `+9665…`). A number that can't be normalised is logged and skipped. | The gateway expects `+9665…`, and stored numbers aren't guaranteed to be in that form. |
| `Application/AlertText.cs` | Dates use the invariant Gregorian calendar and are labelled `ميلادي`. Riyadh is a fixed UTC+3. `HI`/`LO` have their own wording. | `ar-SA` formatting depends on the machine's ICU: one image shows a Hijri year, another Gregorian. `FindSystemTimeZoneById("Asia/Riyadh")` throws on images without tzdata. `new CultureInfo("ar-SA")` throws under invariant globalisation. |
| `Application/SyncWorker.cs` | The exception filter is now `!stoppingToken.IsCancellationRequested`. Sync and sweep are wrapped separately. | An `HttpClient` timeout is a `TaskCanceledException`. The old filter (`is not OperationCanceledException`) let it escape, and a background service exception **stops the host**. Also, a failed sync skipped the alert sweep. |
| `Program.cs` | The SMS client has a 10 s timeout and deliberately no retry handler. | The default timeout is 100 s. A retry on a non-idempotent POST would be a duplicate SMS. |

### Data correctness
| File | What | Why |
|---|---|---|
| `Infrastructure/HttpBaseAddress.cs`, `Program.cs` | A trailing slash is added to base addresses. | `https://meters.example.net/v2` + `meters/x/readings` resolves to `https://meters.example.net/meters/x/readings`, which drops `/v2`. No vendor call would have worked. |
| `Application/MeterSyncService.cs` | Each meter is fetched and saved on its own, with failures handled per meter. A unique violation from another pod is treated as "already stored". Duplicate IDs in a response are removed. Known-ID lookup covers only the IDs in the response, using a `HashSet`. | Before, one vendor error (a 404 for one serial, say) aborted the whole run. Two pods inserting the same `VendorId` made the single `SaveChanges` fail for **all** meters. The query loaded every stored ID of the meter's whole history, and `List.Contains` made the check O(n·m). |
| `Infrastructure/SyncDbContext.cs` | Added foreign keys from readings and meters to patients and meters, indexes on `readings.MeterId` and `PatientId`, `GENERATED ALWAYS AS IDENTITY`, and a `timestamptz` claim column. | There were no FKs, so orphan rows were possible and `phones[reading.PatientId]` could throw `KeyNotFoundException`. |
| `Application/MeterLinker.cs` | A concurrent duplicate link (unique violation) returns 409. | It was check-then-insert, so two requests or pods raced and the loser got a 500. |

### Security
| File | What | Why |
|---|---|---|
| `Application/MeterLinker.cs`, `Api/MeterEndpoints.cs` | `POST /meters` only links when the national ID belongs to the caller (`sub`). Otherwise it returns 404, the same as an unknown ID. | IDOR: any patient could attach a meter to another patient's record. The victim would then get SMS for someone else's readings. The 404/201 difference also showed which national IDs are registered. |
| `Program.cs` | The audience is validated (`meter-sync`). | `ValidateAudience = false` accepted any token from the realm, including tokens issued to other apps. |
| `Program.cs` | CORS is limited to `Cors:Origins`, with no credentials and only the headers and methods the API uses. | `SetIsOriginAllowed(_ => true)` + `AllowCredentials()` let any website make credentialed calls. |
| `Program.cs` | Added ProblemDetails exception handler, status-code pages, HSTS outside Development, and `nosniff` / `X-Frame-Options` / CSP headers. | Unhandled errors had no structured response, and the security headers were missing. |
| `Api/CurrentPatient.cs`, `Api/MeterEndpoints.cs` | A non-numeric `sub` returns 403. | `int.Parse` on it caused a 500. |
| `Api/MeterEndpoints.cs` | `nationalId` is validated (required, ≤ 10 chars). Both fields are trimmed. | A null `nationalId` reached the query. |

### Hosting and DI
| File | What | Why |
|---|---|---|
| `Program.cs` | `PatientDirectory` is now scoped (it was a singleton). | A singleton holding a scoped `DbContext` meant one context shared by every request at once (not thread-safe, and its tracker never reset). |
| `Program.cs` | Settings are still read inside the option callbacks, so test and deployment overrides apply. They are validated once after `Build()`, so a missing setting fails at startup. | Fail fast. |
| `MeterSync.csproj` | Turned on NuGet audit (`all`), with NU1901–NU1904 and nullable warnings treated as errors. | The zero-known-vulnerabilities policy. There are none today. |

## Found, not fixed
- **Alerts for historical readings.** The vendor returns a meter's whole history. On a patient's first sync, they get one SMS for every old out-of-range reading. After an outage, they get a backlog of stale ones. The fix is a product rule I didn't want to guess: for example, "no SMS for readings taken before the meter was linked" or "only for readings from the last N hours". Ask the care team.
- **A second-hand meter's history goes to the new owner.** Readings taken before `LinkedAt` belong to the previous owner, but they are imported under the new owner. Unlinking and transfer aren't supported either.
- **A send that times out may still go out.** If the gateway accepts a message but the response times out, the claim is released and the SMS is sent again. That can't be fixed without an idempotency key from the gateway, and I chose a possible duplicate over a lost alert.
- **PII at rest.** `NationalId` and `Phone` are stored in plain text. Encrypting them needs key management, plus a deterministic hash for the national-ID lookup.
- **Schema delivery.** There are no migrations or reviewed SQL scripts (the tests create the schema from the model). Production needs a reviewed up/down script, including the new columns, FKs and identity change.
- **Scale.** Every pod calls the vendor for every meter every minute. That's correct now, but wasteful: there's no paging over meters and no lease that splits meters between pods.
- **Layering and style.** `PatientDirectory` (Infrastructure) returns an Api DTO. The services use `DbContext` directly instead of repositories. The EF configuration is inline rather than in `IEntityTypeConfiguration` classes. There are no XML docs on some public members.
- **No HTTPS redirection.** I left it out because TLS is likely terminated at the ingress. HSTS is on.
- **Untested here.** The test order wasn't strict red-green. I wrote the tests first but didn't run them red before implementing. Nothing that needs a database ran (sweeper claims, sync, linking).
