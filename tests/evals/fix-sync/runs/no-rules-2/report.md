I fixed the service in place and `MeterSync/` builds with 0 warnings. I added 10 unit tests in `MeterSync.Tests/` and they pass. Nothing that needs PostgreSQL has run. That includes the advisory lock in `MeterSyncService.cs`, the `FOR UPDATE SKIP LOCKED` query in `AlertSweeper.cs`, and the unique-violation handling in the link endpoint, so none of it is tested here.

## Fixed

**Security**
- **`Program.cs`, JWT audience:** `ValidateAudience` was `false`. It now requires the audience `meter-sync`, so tokens issued for other apps on the same identity server are rejected.
- **`Program.cs`, CORS:** the policy allowed every origin together with credentials. It now allows only the origins in `Cors:Origins`.
- **`Api/MeterEndpoints.cs`, link meter:** any logged-in patient could link a meter to any other patient's national ID. That would have routed the other patient's readings and SMS alerts to the wrong person. The endpoint now only links to the caller's own patient record, and anything else returns 404.
- **`Api/CurrentPatient.cs`:** a missing or non-numeric `sub` threw a 500. It now returns 401 on both endpoints.

**Clinical correctness**
- **`MeterSyncService.cs`, HI/LO readings:** `HI` and `LO` were skipped as "not a number", so the most dangerous readings never produced an alert. They are now stored as 601 and 19 mg/dL (just past the meter's limits), which trigger High and Low alerts. This is in a new file, `Application/VendorReadingParser.cs`.
- **`MeterSyncService.cs`, mmol/L:** these values were compared against the mg/dL thresholds, so a normal 5.5 mmol/L raised a false Low alert. They are now multiplied by 18 first.
- **`MeterSyncService.cs`, parsing:** readings with an unknown unit, a negative value, a decimal comma, or fields too long for their column are skipped with a warning instead of crashing the sync. The vendor DTO now accepts a missing `value` or `unit`.
- **`AlertSweeper.cs`, old readings:** a newly linked meter uploads its whole history, which would have texted the patient about every old out-of-range reading. Alerts are now sent only for readings from the last 6 hours (my choice of window; change it if the care team wants another).
- **`AlertText.cs`, date format:** the `ar-SA` culture formats dates in the Hijri calendar. The date is now Gregorian with Western digits, in Riyadh time. It falls back to a fixed UTC+3 offset if the container has no tz database.

**Several pods**
- **`AlertSweeper.cs`, duplicate SMS:** every pod selected the same unsent readings, so patients got duplicate SMS (the gateway has no idempotency key). Each reading is now claimed with `FOR UPDATE SKIP LOCKED` in a transaction, sent, marked sent and committed, so pods share the backlog without double-sending. If a pod dies between the send and the commit, that one reading can be sent twice. I chose that over risking a missed alert.
- **`AlertSweeper.cs`, stuck queue:** one undeliverable reading (for example no phone, or the gateway rejecting it) made every run fail on it and starved all later alerts. It is now logged, skipped for that run and retried on the next one.
- **`MeterSyncService.cs`, concurrent syncs:** two pods syncing at once hit the unique index on `VendorId` and lost the whole batch. A Postgres advisory lock now lets only one pod sync at a time; the other returns 0.
- **`MeterSyncService.cs`, failure isolation and dedupe:** one failing meter no longer aborts the others, and the check for already-stored readings is now a single indexed lookup rather than loading the meter's whole history.
- **`Api/MeterEndpoints.cs`, link race:** two simultaneous requests linking the same serial number gave a 500 from the unique index. That case now returns 409.

**Other**
- **`Program.cs`, vendor URL:** the configured base address `https://meters.example.net/v2` has no trailing slash, so requests went to `/meters/...` instead of `/v2/meters/...`. A trailing slash is now added in code (the setting itself is unchanged).
- **`Program.cs`, SMS client:** it has an explicit 20s timeout and deliberately no retry handler, because a retried POST could send two texts.
- **`Application/SyncWorker.cs`:** a vendor outage stopped the alert sweep for readings already stored. The two steps are now independent, and shutdown no longer throws.
- **`Api/MeterEndpoints.cs`:** the serial number is trimmed before it is validated and stored.

## Found but not fixed
- **No schema migrations:** the tests build the schema from the model, but production needs migrations or a deployment script, which I didn't add.
- **`Raw` and `Unit` in the SMS text:** the `Unit` is inserted into the Arabic message as the vendor sent it ("mg/dL"). I didn't translate it.
- **No readiness check:** only `/health/live` exists, and there is no rate limiting or audit logging on the API.
- **`TakenAt` not validated:** a reading dated in the future is accepted.
- **SMS cost:** an alert is sent for every out-of-range reading, so a patient with several in a row gets several texts. The care team may want some throttling.
- **Thresholds:** the 70 and 250 mg/dL thresholds are hard-coded.