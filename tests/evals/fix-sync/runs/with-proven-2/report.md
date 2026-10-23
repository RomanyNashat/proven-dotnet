I applied all of the review's findings that can be fixed to `/tmp/claude-0/syncruns/with-proven-2/MeterSync`. It builds with no warnings (`-warnaserror`) and the 110 new tests pass; none of them needs a database. I could not write `../report.md`: the harness refused report files from me, so the report is below. Please save it as `/tmp/claude-0/syncruns/with-proven-2/report.md`.

The raw SQL has never run against PostgreSQL: the alert claim (`UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING "Id" AS "Value"`) and the bulk insert (`unnest` + `ON CONFLICT`). Tests check that its column names match the model and that Npgsql can send each array type, but the hidden PostgreSQL tests will be its first real run. The two-pod behaviour is reasoned, not tested.

Everything task.md says to keep still holds, and production's settings are unchanged. I loaded the cited skills first: dotnet-core, auth-patterns, api-design, worker-patterns, outbox, postgresql-patterns, localization, owasp-aspnetcore, pii-masking, efcore-patterns.

# MeterSync: report

## Fixed (by review finding number)
- **#1 `Api/MeterEndpoints.cs`:** `POST /meters` loads the caller's patient from `sub` and returns 404 if it doesn't exist or its NationalId differs from the body's. Before, any caller could link a meter to any patient, and the answers showed which national IDs exist.
- **#2 `Program.cs`:** `PatientDirectory` is now scoped. As a singleton it held one DbContext, so concurrent requests failed.
- **#3 `Infrastructure/HttpBaseAddress.cs` + `Program.cs`:** a trailing `/` is added to both base addresses; the setting is unchanged. Without it, `/v2` was dropped from every vendor call and nothing synced.
- **#4 `Domain/Reading.cs`:** `HI` is stored as High at 600 and `LO` as Low at 20, with Raw kept. They were being dropped.
- **#5 `Domain/Reading.cs`:** `TryFromMeter` converts mmol/L ×18, rounds to one decimal, and rejects unknown units. Before, 15 mmol/L produced a false Low.
- **#6 `Infrastructure/AlertClaims.cs`, `Application/AlertSweeper.cs`:** each run claims its alerts with a 10-minute lease, committed at once, then sends and marks each one, and stops a minute before the lease lapses. Every pod had been sending the same SMS.
  - Trade-off: a pod that dies between sending and marking causes one resend once the lease expires.
- **#7 Sweeper, Reading:** one failure no longer stops the batch.
  - Each reading has its own try/catch and an attempt count; after 5 failures, or at once if there's no phone, it is moved aside as `Failed`.
  - The catch uses `when (!ct.IsCancellationRequested)`, because an HttpClient timeout is an `OperationCanceledException` and would otherwise abort the whole batch.
- **#8 `MeterSyncService.cs`, `SyncWorker.cs`:** fetch and insert run per meter, each in its own try, with values validated before insert. Sync and sweep are in separate try blocks. One bad meter used to lose the whole run.
- **#9 `Infrastructure/ReadingWriter.cs`:** a single `INSERT ... SELECT * FROM unnest(...) ON CONFLICT ("VendorId") DO NOTHING` per meter, all values parameterized. Two pods inserting the same reading no longer roll back a batch.
- **#10 `AlertText.cs`, `RiyadhTime.cs`:** Riyadh time falls back to a fixed UTC+3 when tzdata is missing. The date is `dd/MM/yyyy HH:mm` in the invariant culture: Gregorian, Latin digits, no ICU needed.
- **#11 `Program.cs`:** the audience `meter-sync` is validated.
- **#12 `Program.cs`:** CORS allows only `Cors:Origins`, GET/POST, the Authorization and Content-Type headers, and no credentials.
- **#13 `Reading.cs`:** out-of-range readings taken more than 24 h before linking are stored but not texted. **The 24 h window is my placeholder; the product team should set it.**
- **#15 `Meter.cs`, `SyncDbContext.cs`, endpoint:** a new `SerialKey` column (trimmed, upper-case) carries the unique index, so serials are unique regardless of case. The serial is kept as sent for `GET /meters`. A duplicate-key error on save returns 409 instead of 500.
- **#16 `SyncDbContext.cs`:** foreign keys from meters and readings to patients, and from readings to meters, all `ON DELETE RESTRICT`.
- **#17 `MeterSyncService.cs`:** only the incoming vendor IDs are looked up, using the unique index; the foreign key adds the `MeterId` index.
- **#18:** logs carry the vendor ID, a reason and the error type, never the glucose value. Default HttpClient logging is removed from both clients (it wrote the serial in the URI).
- **#19 `CurrentPatient.cs`:** claim mapping is off and `sub` is parsed safely; a missing or non-numeric `sub` returns 401 instead of 500.
- **#20 `UnexpectedExceptionHandler.cs`, `SecurityHeaders.cs`, `Program.cs`:**
  - errors are ProblemDetails with a traceId and nothing internal;
  - security headers are set in `OnStarting`, so 500s get them too, including `Cache-Control: no-store`;
  - HSTS is 365 days outside Development.
- **#21 `SyncDatabase.cs`:** `Maximum Pool Size=20` is added when the connection string doesn't set one. **20 is a placeholder to agree with the DBA.**
- **#22 `Program.cs`, `HttpServiceOptions.cs`, `SyncDatabaseOptions.cs`:** settings are checked at start-up:
  - both base addresses (required, absolute URLs);
  - `ConnectionStrings:Sync`;
  - an authority or explicit signing keys.
- **#23 `Program.cs`:** scope and build validation are on in every environment. A test proves a singleton holding a DbContext now stops start-up in Production.
- **#24:** a partial index `ix_readings_alert_pending` on pending alerts replaces `(Alert, AlertSentAt)`. The claim SQL uses the literal `'Pending'` so the index can be used.
- **#25:** `PatientDirectory` returns an Application read model (`LinkedMeter`); the JSON shape is unchanged.
- **#26:** a per-patient (`sub`) fixed-window limit on `POST /meters`: 30 per minute per pod, returning a 429 ProblemDetails. I kept it generous so a burst of concurrent links still sees 201/409.
- **#27 `LinkMeterValidation.cs`:** NationalId must be 10 ASCII digits and the serial 1–40 characters, otherwise 400.
- **#28:** the Arabic SMS text fits one part (66 characters worst case, tested at 70 or under), with its own wording for HI and LO.
- **#29 `Database/001_initial.up.sql` / `.down.sql`:** a reviewed up/down pair, with identity ALWAYS keys. A test fails if the script and the model drift. The app still never migrates.

**Tests** (`/tmp/claude-0/syncruns/with-proven-2/MeterSync.Tests/`):
- Unit tests for parsing and units, HI/LO, history, retry state, the SMS text, claims and validation, pool sizing and base addresses.
- Model tests: foreign keys, identity ALWAYS, bounded strings, the partial index, the raw-SQL column names, array type mappings.
- `WebApplicationFactory<Program>` tests in Production with the database on a closed port:
  - 401 for a wrong audience or a bad `sub`;
  - a 500 ProblemDetails that leaks nothing;
  - 400 and 429;
  - CORS for an allowed and a foreign origin;
  - security headers;
  - the vendor call going to `https://meters.example.net/v2/meters/...`;
  - start-up failing on missing settings and on a captive DbContext.

## Found, not fixed
- **#14, whoever links a serial first gets its readings:** fixing it needs proof of possession (a pairing code), which the fixed request shape doesn't allow. It's for product and the vendor.
- **Who calls `POST /meters`?** I assumed the patient. If clinic staff link meters, this needs a staff policy instead of the national-ID match.
- **Every pod polls the vendor every minute:** the data stays correct, but vendor load grows with the pod count. An advisory lock would hold a transaction across all the HTTP calls; I didn't add one.
- **HSTS behind nginx:** `UseHsts` sends nothing over plain HTTP, and trusting forwarded headers needs the ingress's network, which isn't in the folder. Agree with SRE which layer sends HSTS. HTTPS redirection is left out for the same reason.
- **Rejected vendor readings** are logged again every minute because they aren't stored.
- **A timed-out SMS** may already have been delivered, so the retry can send it twice. The gateway has no idempotency key.
- **Stale uploads:** readings a meter uploads days late still alert if they were taken after linking.
- **An existing database:** `001_initial` assumes an empty one. A schema already in production would need a delta script (`SerialKey` backfill, `CREATE INDEX CONCURRENTLY`).
- **Packages:** still pinned at 10.0.0, with no known vulnerabilities; newer patch versions exist.
- **Architecture:** `LinkAsync` still uses the DbContext directly; moving it to an Application handler is a later refactor.

## Not verified
- Nothing ran against PostgreSQL.
- Coverage is 69% of lines and 70% of branches, under the 80% line target. The uncovered code is what needs a database: `MeterSyncService`, `AlertSweeper`, `AlertClaims`, `ReadingWriter`, most of `LinkAsync`. Covering it needs Testcontainers, and the in-memory provider is banned.

## Notes for the caller
- The scratchpad `/tmp/claude-0/-home-claude/.../scratchpad` is shared with concurrent runs: another run (pointing at with-proven-1) overwrote my `gen` folder. I didn't touch its files and kept all my work inside with-proven-2.
- No journal entry was written, to keep the folder limited to the task.