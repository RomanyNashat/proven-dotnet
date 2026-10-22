# Grade: diff-mode review eval (9 reports)

Reference for line numbers: `ReadingExport.cs` as the diff adds it (38 lines). Diff line N = file line N-25. Key file lines: 2-4 usings, 9 class, 11 `AllAsync`, 13-16 query (14 `Where`, 15 `OrderBy`), 18 header, 21 `CollectedAt`, 22 `Value`, 23 `Unit`, 24 `Notes`, 27 NationalId log, 32 `OneAsync`, 34 `queries.GetAsync`, 36 interpolated CSV row.

Rule used for "old flaws listed": counted only when the report asks for the old flaw itself to be fixed as part of this change. A mention as the consequence of a new path (for example "the existing ErrorMiddleware sends ex.Message back") is not counted. A Known-traps row that is explicitly dismissed for this change is not counted.

---

## run1-code

1. **C1 yes**: "**CSV correctness and injection (wrote)** `ReadingExport.cs:21-24, 36`. `Notes` and `Unit` are free text written raw."
   **C2 yes**: "**PII in logs (wrote)** `Application/ReadingExport.cs:27`. `logger.LogInformation("Export for {NationalId}…"`"
   **C3 yes**: "**Security / IDOR (reaches)** ... The new `GET /readings/{id}/export` calls `ReadingQueries.GetAsync`, which filters only on `r.Id == id`"
2. **Old flaws listed: none.** The ErrorMiddleware appears only as a consequence: "the request throws and the existing `ErrorMiddleware` sends `ex.Message` back to the client" (the fix only changes the binding). ValidateScopes is dismissed: "Dismissed for this change."
3. **Count line: present, N = 9** ("Outside this change: 9.")
4. **Labels: no.** Critical 1-2 and Warnings 1-7 are labeled, but both Suggestions are not: "1. `ReadingEndpoints.cs:27, 30`: send `text/csv; charset=utf-8`..." and "2. `ReadingExport.cs:11`: the public method `AllAsync` has no XML doc".
5. **Line numbers: all right** (:34, :27, :22, :36, :21-24, :13-16, :2-4, :9, :13, :18, :11).

## run1-security

1. **C1 yes**: "**A05 CSV/formula injection and row forgery (wrote)** at `HomeReadings/Application/ReadingExport.cs:21-24` and `:36`."
   **C2 yes**: "**A09 PII in logs (wrote)** at `HomeReadings/Application/ReadingExport.cs:27`. ... every export logs `{NationalId}` in clear"
   **C3 yes**: "**A01 IDOR (reaches)** ... the new `GET /readings/{id}/export` calls `ReadingQueries.GetAsync`, which filters only on `r.Id == id`"
2. **Old flaws listed: 1 (H12, error middleware).** Finding 6 is headed by the leak itself, and its fix asks for the middleware to change: "**A10 error detail reaches the client (reaches)** ... via `HomeReadings/Api/ErrorMiddleware.cs:16`" / "**Fix:** take `DateTimeOffset` ... and make `ErrorMiddleware` return ProblemDetails with only a `traceId`." The DateTime Kind part of the finding is legitimate. Asking for the ErrorMiddleware change is the old flaw. (Borderline: it is tied to a new path, but the fix raises H12 itself.)
3. **Count line: present, N = 11**
4. **Labels: yes.** All 7 findings carry wrote or reaches.
5. **Line numbers: all right** (:32-36, :27, :21-24, :36, :13-16, :27-36).

## run1-dba

1. **C1 yes**: "**CSV not escaped (wrote)** `ReadingExport.cs:49` and `:61` write `Notes` (free text) and `Unit` without quoting."
   **C2 yes**: "**PHI in logs, and the full entity loaded to get it (wrote)** `ReadingExport.cs:52` logs `NationalId`."
   **C3 yes**: "**Ownership / IDOR (reaches)** `HomeReadings/Application/ReadingExport.cs:59` calls `ReadingQueries.GetAsync` ... filters on `r.Id == id` only"
2. **Old flaws listed: none.** The UTC-day point ("the range is a UTC day, not the patient's day") is about the new `from`/`to`, not the old H07 code. ValidateScopes: "dismissed for this change".
3. **Count line: present, N = 5**
4. **Labels: yes.** Findings 1-6 all carry wrote or reaches.
5. **Line numbers: all wrong.** Every `ReadingExport.cs` number is the diff's line number, not the file's (+25):
   - :59 should be 34
   - :39 should be 14
   - :47 should be 22
   - :61 should be 36 (twice, plus "for line 61")
   - :52 should be 27
   - :49 should be 24
   - :36-53 should be 11-28

## run2-code

1. **C1 yes**: "**Correctness/security: CSV not escaped, formula injection.** `ReadingExport.cs:46-49` and `:61`. *Wrote.* `Notes` (patient free text) and `Unit` go into the CSV raw"
   **C2 yes**: "**Security: PII in logs.** `ReadingExport.cs:52`. *Wrote.* It logs `NationalId` at Information"
   **C3 yes**: "**Security: IDOR on health data.** ... *Reaches.* `OneAsync` calls `ReadingQueries.GetAsync`, which filters by `r.Id == id` only"
2. **Old flaws listed: none.** The ErrorMiddleware appears only as a consequence: "The result is a 500, and the existing `ErrorMiddleware` (`:16`) then sends `ex.Message` back to the client." ValidateScopes: "dismissed for this change".
3. **Count line: present, N = 16**
4. **Labels: yes.** Findings 1-10 all carry Wrote, Reaches or both.
5. **Line numbers: mostly wrong (diff numbering, +25).** Only `:2-3` is right.
   - :59 should be 34
   - :52 should be 27
   - :46-49 should be 21-24
   - :61 should be 36 (in #3, #4, #9)
   - :47 should be 22
   - :38-41 should be 13-16
   - :34 (#7, the class with two data paths) should be 9
   - :36,43 should be 11, 18

## run2-security

1. **C1 yes**: "**A05 CSV/formula injection and row forgery (wrote)**: `ReadingExport.cs:21-24` and `:36`"
   **C2 yes**: "**A09 PII in logs (wrote)**: `HomeReadings/Application/ReadingExport.cs:27` ... logs the patient's national ID in clear"
   **C3 yes**: "**A01 IDOR (wrote + reaches)** ... calls `ReadingQueries.GetAsync(id)` ... filters on `r.Id == id` only and never checks `PatientId`"
2. **Old flaws listed: 1 (H12, error middleware).** It is a bullet in the fix of finding 4: "The general `ErrorMiddleware` leak is older code. Replace it with ProblemDetails plus a trace id." The report calls it older code and still lists it as something to fix. The mention of `Notes` as "unbounded" (H13) is description only, not counted. ValidateScopes is dismissed ("this change neither writes nor reaches a flaw through it"), though the report adds "It is still worth fixing on its own".
3. **Count line: present, N = 9**
4. **Labels: yes.** All 6 findings are labeled.
5. **Line numbers: all right** (:34, :27, :21-24, :36, :13-16, :11, :32, :22).

## run2-dba

1. **C1 yes**: "**[CSV escaping]** `Application/ReadingExport.cs:21-24, 36`. **Wrote.** `Notes` and `Unit` are written unquoted."
   **C2 yes**: "**[PHI in logs]** `Application/ReadingExport.cs:27`. **Wrote.** The line logs `NationalId` in plain text"
   **C3 yes**: "**[Ownership / IDOR]** ... **Reaches.** `OneAsync` calls `queries.GetAsync(id)`, and that query filters only on `r.Id == id`"
2. **Old flaws listed: none.** The Riyadh-day point is about the new range. ValidateScopes: "dismissed for this change".
3. **Count line: present, N = 8**
4. **Labels: no.** Findings 1-6 are labeled. Suggestion 7 is not: "7. `Api/ReadingEndpoints.cs:26`: add `Content-Disposition: attachment; filename=...`".
5. **Line numbers: all right** (:34, :27, :13-16, :14, :22, :36, :21-24).

## run3-code

1. **C1 yes**: "**CSV escaping / formula injection (wrote)** `ReadingExport.cs:23-24, 36`. `Unit` and `Notes` are written raw."
   **C2 yes**: "**PHI in logs (wrote)** `HomeReadings/Application/ReadingExport.cs:27`. `LogInformation("Export for {NationalId}..."`"
   **C3 yes**: "**IDOR / ownership (reaches)** ... calls the existing `ReadingQueries.GetAsync` ... filters only on `r.Id == id`, never on `PatientId`."
2. **Old flaws listed: none from the key.** Note: finding 3's fix adds "Separately, the middleware should set the UI culture only." `LanguageMiddleware` is not in the key's old list (the key counts the ar-SA formatting as the change's), so it is not counted. A strict reader may see it as an old-code fix slipped into a change finding.
3. **Count line: present, N = 6**
4. **Labels: yes.** Findings 1-11 are all labeled.
5. **Line numbers: all right** (:34, :27, :22, :36, :23-24, :13-16, :2-3, :18, :11).

## run3-security

1. **C1 yes**: "**A05 CSV and formula injection in a file that goes to a doctor.** Type: *wrote*. At `ReadingExport.cs:21-24` and `:36`, `Notes` and `Unit` ... are written with no quoting or escaping."
   **C2 yes**: "**A09 PII in logs: national ID logged in clear.** Type: *wrote*. `ReadingExport.cs:27` logs `{NationalId}` on every export."
   **C3 yes**: "**A01 IDOR: one patient can export another patient's reading.** Type: *reaches*. ... calls the existing `ReadingQueries.GetAsync`, which filters only on `r.Id == id`"
2. **Old flaws listed: 1 (H12, error middleware).** Finding 5 is headed "**A10 / A06: unvalidated date range, and errors leak to the client.**", and its fix says: "Separately, ErrorMiddleware should return ProblemDetails with a trace id and nothing internal."
3. **Count line: present, N = 8**
4. **Labels: yes** for findings 1-6. Item 7 (the ValidateScopes Known-traps item) has no label, but it is dismissed ("dismissed as a security finding for this change"), not a finding. It is numbered inside the Suggestion list, though, which makes it look like a seventh finding.
5. **Line numbers: all right** (:34, :27, :13-16, :21-24, :36, :22).

## run3-dba

1. **C1 yes**: "`ReadingExport.cs:21-24, 36`. **Wrote.** `Notes` and `Unit` are written raw: a comma or newline in `Notes` breaks the row, and a leading `=`/`+`/`-`/`@` is a CSV formula injection." (Rated only a Suggestion.)
   **C2 yes**: "**PII in logs** `ReadingExport.cs:27`. **Wrote.** It logs the patient's `NationalId`"
   **C3 yes**: "**Ownership / IDOR** ... **Reaches.** The new `GET /readings/{id}/export` calls `ReadingQueries.GetAsync(id)`, which filters on `r.Id == id` only"
2. **Old flaws listed: none.** ValidateScopes is dismissed ("this change neither wrote it nor reaches it"), though the report adds "is still worth fixing".
3. **Count line: present, N = 8**
4. **Labels: no.** Suggestion 2 has no label: "`ReadingExport.cs:15`. `OrderBy(CollectedAt)` alone gives an undefined order ... Add `.ThenBy(r => r.Id)`".
5. **Line numbers: all right** (:34, :14, :13-16, :18-25, :27, :22, :36, :21-24, :15).

---

## Summary table

| Report | C1 | C2 | C3 | Old flaws listed | Count line | Labels on every finding | ReadingExport line numbers |
|---|---|---|---|---|---|---|---|
| run1-code | yes | yes | yes | 0 | 9 | no (2 suggestions) | right |
| run1-security | yes | yes | yes | 1 (H12) | 11 | yes | right |
| run1-dba | yes | yes | yes | 0 | 5 | yes | **all off** (diff numbering, +25) |
| run2-code | yes | yes | yes | 0 | 16 | yes | **mostly off** (+25; only :2-3 right) |
| run2-security | yes | yes | yes | 1 (H12) | 9 | yes | right |
| run2-dba | yes | yes | yes | 0 | 8 | no (suggestion 7) | right |
| run3-code | yes | yes | yes | 0 | 6 | yes | right |
| run3-security | yes | yes | yes | 1 (H12) | 8 | yes | right |
| run3-dba | yes | yes | yes | 0 | 8 | no (suggestion 2) | right |

## Totals

- **Change findings found: 27 / 27.** Every report has C1, C2 and C3. C1 covers both `AllAsync` and `OneAsync` in all nine.
- **Old flaws listed: 3 in all**, the same one each time: H12 (error middleware), once in each security report. In each case it is attached to a legitimate finding (the `from`/`to` DateTime Kind failure), but the fix also asks for `ErrorMiddleware` to be replaced. The code reviewers mention the same middleware only as the consequence and do not ask for it to be fixed, which is the correct form. No report lists H02, H03, H04, H07, H08, H09, H10, H11, H13, the POST `collectedAt` Kind, or ValidateScopes as a finding.
- **Count line: present in 9 / 9.** N varies a lot: code 9 / 16 / 6, security 11 / 9 / 8, dba 5 / 8 / 8. The key has 12 old flaws. run2-code's 16 is more than the key holds, so it counts things the key doesn't (or splits items). N is not consistent between runs of the same reviewer.
- **Labels: 6 / 9 reports label every finding.** The three misses are all low-severity Suggestions (run1-code ×2, run2-dba ×1, run3-dba ×1).
- **Line numbers: 7 / 9 right.** run1-dba (all) and run2-code (all but one) used the diff's line numbers for `ReadingExport.cs` instead of the file's, off by exactly 25 each time. The other seven are correct throughout.

## Other things a reader should know

- **ValidateScopes:** all nine reports handle it the same way, as a separate "Known-traps row" dismissed for this change. Three of them add "still worth fixing" (run2-security, run3-dba), or put it in the numbered list (run3-security #7). None makes it a finding of the change.
- **Legitimate extras (allowed by the key) are well covered:** the DateTime Kind failure against Npgsql is in 9 / 9, no range limit in 9 / 9, and the ar-SA formatting of the value in 9 / 9.
- **Extras outside the key:** none of these is an old flaw from the key's list, so none is counted as one.
  - Layering (Application → Api/Infra): run1-code, run2-code, run3-code. All label it "wrote", for the new class.
  - Missing tests: all three code reports.
  - Cache-Control / Content-Disposition: several reports.
  - PHI export audit: run1-security, run2-security.
  - `ThenBy` ordering: run3-dba.
  - run3-code also suggests the `LanguageMiddleware` should set the UI culture only, a fix to older code that the key doesn't list.
- **Severity varies for the same finding.** C1 ranges from Critical (run2-code) to a Suggestion (run3-dba). C2 ranges from Critical (most reports) to a Warning (run3-security, run3-dba).
- **Run notes in the reports:** several reports say they resumed after a usage-limit reset (run1-security, run2-dba, run3-dba). run3-code ends with a stray "For memory: nothing new about the user." line.
