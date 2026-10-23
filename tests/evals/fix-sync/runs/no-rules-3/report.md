I fixed the problems below in place, and `MeterSync/` builds with 0 warnings and 0 errors. Nothing has run against PostgreSQL, since there isn't one here, and I didn't add any unit tests. That means the sweeper's raw SQL, the unique-violation handling and the multi-pod behavior are untested.

## Fixed

| File | Problem | Why it matters |
|---|---|---|
| `Program.cs` | `MeterVendor:BaseAddress` (`…/v2`) has no trailing slash, so `meters/{serial}/readings` resolved to `https://meters.example.net/meters/…` and dropped `/v2`. Both base addresses now get a trailing `/`. | Every vendor call went to the wrong URL. |
| `Program.cs` | JWT audience validation was off. It now requires `meter-sync`. | Tokens issued for other services were accepted. |
| `Program.cs` | CORS allowed any origin with credentials. It is now limited to `Cors:Origins`. | Any website could call the API with a patient's credentials. |
| `Program.cs` | The SMS client now has a 15-second timeout. It still has no retry handler. | The gateway has no idempotency key, so a retried POST would text the patient twice. |
| `MeterEndpoints.cs` | `POST /meters` linked a meter to whatever national ID was in the body, so any logged-in patient could attach meters to someone else. It now requires the national ID to belong to the caller's `sub`, and returns 404 if it doesn't. | Patient data and alerts could be redirected to the wrong person. |
| `MeterEndpoints.cs` | The serial number is trimmed. A unique-violation race on the serial now returns 409 instead of 500. | Two requests linking the same serial at once crashed one of them. |
| `CurrentPatient.cs`, `MeterEndpoints.cs` | A missing or non-numeric `sub` caused `int.Parse` to throw a 500 (or a null dereference). It now returns 401. | A bad token should be rejected, not crash the request. |
| `MeterSyncService.cs` | `HI` and `LO` readings were skipped because they aren't numbers. They are now stored as 600 and 20 mg/dL, so they raise High and Low alerts. | The most dangerous readings never produced an alert. |
| `MeterSyncService.cs` | `mmol/L` readings were never converted, so 5.6 was stored as 5.6 mg/dL and flagged Low. They are now multiplied by 18; unknown units, out-of-range values and over-long fields are skipped with a warning. | Wrong alerts, and the thresholds are in mg/dL. |
| `MeterSyncService.cs` | The duplicate check was per meter and one bad insert lost the whole batch. Two pods syncing together hit the unique index on `VendorId` and failed on every run. It now checks globally with a hash set, saves each reading on its own, and ignores a unique violation from another pod. | Sync would have failed repeatedly with two pods. |
| `MeterSyncService.cs` | One meter's vendor error aborted the whole sync. Each meter now syncs in its own try/catch. | One broken meter blocked everyone else's readings. |
| `AlertSweeper.cs` | Two pods read the same unsent alerts and both texted the patient. Each reading is now claimed with `SELECT … FOR UPDATE SKIP LOCKED` in a transaction that stays open until the SMS is sent and recorded. | Patients got duplicate SMS, and the gateway can't dedupe. |
| `AlertSweeper.cs` | A reading whose patient is missing or whose SMS fails no longer stops the whole sweep. The failure is logged, the reading is skipped for that run and retried on the next. | Before, one bad reading blocked every alert behind it. |
| `SyncWorker.cs` | The alert sweep is now in its own try/catch. | A vendor outage was skipping the sweep, so alerts already due weren't sent. |
| `AlertText.cs` | `ar-SA` formatting printed Hijri dates, and `FindSystemTimeZoneById("Asia/Riyadh")` throws where tzdata or ICU is missing. It now uses a fixed UTC+3 offset and the invariant culture, so the date is Gregorian in Riyadh time. | Patients got wrong-looking dates, or the message failed to build in a slim container. |

## Found but not fixed

- **Old readings alert on first sync.** Linking a meter pulls its full history, and every old out-of-range reading gets an SMS. Fixing it means choosing a rule, such as ignoring readings older than N hours or older than `LinkedAt`. That is a product decision, and a test using old fixture timestamps could break on it.
- **Each sync downloads the vendor's full history for every meter, every minute.** The vendor API has no "since" parameter, so this will get heavier as readings accumulate.
- **Every pod runs the sync worker.** It is correct now but wasteful, because the pods duplicate the vendor calls. A single-leader lock would avoid that.
- **No migrations.** The service never creates or updates the schema, and `Meter.PatientId` and `Reading` have no foreign keys. Deleting a patient leaves orphaned rows.
- **Duplicate SMS after a crash.** If a pod crashes after the SMS is sent but before the commit, the patient is texted again. I chose that over losing an alert.
- **Alert text for `HI` and `LO`.** The SMS shows the raw value, so it reads "HI mg/dL" inside Arabic text. A dedicated Arabic phrase would read better.
- **Stored value for `HI` and `LO`.** The 600 and 20 mg/dL figures are bounds, not true values.