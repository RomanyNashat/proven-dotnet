# /full-review MeterSync — consolidated findings

Sources: known-traps scan (8 hits, all confirmed by reviewers), code-reviewer, security-reviewer, dba-reviewer.

## Critical
C1 Vendor base address `https://meters.example.net/v2` has no trailing slash → every call goes to `/meters/...` (drops `/v2`). Config value must stay; fix in code (Program.cs:19).
C2 HI/LO readings fail decimal.TryParse and are dropped → most dangerous readings never alerted (MeterSyncService.cs:21). HI = over 600 mg/dL, LO = under 20.
C3 mmol/L never converted to mg/dL (×18) → wrong alerts (MeterSyncService.cs:27, Reading.cs:48).
C4 AlertSweeper: every pod selects and texts the same rows; SMS gateway has no idempotency → duplicate SMS (AlertSweeper.cs:16-31).
C5 MeterSyncService: check-then-insert race between pods on unique VendorId; single SaveChanges loses the whole run (MeterSyncService.cs:18-32).
C6 PatientDirectory singleton holds scoped DbContext (Program.cs:13); scope validation off in Production.
C7 POST /meters IDOR: patient looked up by body nationalId, never by token sub (MeterEndpoints.cs:35); also national-ID enumeration.
C8 JWT ValidateAudience = false (Program.cs:30).
C9 CORS SetIsOriginAllowed(_ => true) + AllowCredentials; Cors:Origins never read (Program.cs:38).

## Warning
W1 SyncWorker catch filter lets HttpClient timeout (TaskCanceledException) escape → worker faults → host stops (SyncWorker.cs:21). Sync failure also skips sweep.
W2 One failing meter aborts sync for all meters; one failing SMS blocks all later alerts forever (ordered by Id).
W3 AlertText: plain ar-SA → Hijri or Gregorian by ICU; Asia/Riyadh lookup has no fallback (AlertText.cs:9,13).
W4 POST /meters check-then-insert on SerialNumber → 500 on race; catch 23505 → 409.
W5 Input validation: NationalId unvalidated; serial allows `..` (path segment on vendor URL).
W6 CurrentPatient int.Parse on sub → 500; MapInboundClaims.
W7 No ProblemDetails / exception handler / HSTS / security headers.
W8 No foreign keys; no index on readings.MeterId; sweep index doesn't fit query.
W9 Health values logged (MeterSyncService.cs:23); System.Net.Http logs URLs with serial numbers at Information.
W10 SmsClient has no timeout (100 s default); must not get retries (non-idempotent POST).
W11 Known-ID load per meter is unbounded and List.Contains O(n·m).
W12 NumberStyles.Number accepts thousands separators ("1,2" → 12).
W13 NuGetAudit not enabled as errors; nullable warnings not errors.

## Suggestion / not fixed candidates
S1 Readings from before the meter was linked (vendor returns full history) are imported and alerted.
S2 PII (NationalId, Phone) in plain text at rest; connection string lacks SSL Mode.
S3 No rate limit on POST /meters.
S4 Pool size not set for fleet.
S5 Identity columns BY DEFAULT vs house rule ALWAYS.
S6 Layering: Infrastructure references Api.MeterResponse; endpoints use DbContext directly.

## Test coverage
No test project exists: line 0% ⚠️ (target 80%), branch 0% ⚠️ (target 70%). Next step: /coverage.

## Dependency vulnerabilities
`dotnet list package --vulnerable --include-transitive`: none. `--deprecated`: none.
