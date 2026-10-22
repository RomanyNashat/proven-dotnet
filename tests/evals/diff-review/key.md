# Answer key, diff-mode eval (not given to any run)
In the change (should be findings):
- C1 CSV injection / unquoted CSV: Notes (and Unit) written raw, ReadingExport.cs AllAsync and OneAsync. (wrote)
- C2 national ID logged: ReadingExport.cs "Export for {NationalId}". (wrote)
- C3 GET /readings/{id}/export calls ReadingQueries.GetAsync, which has no owner check: the old IDOR is reachable through a new route. (reaches)
Also legitimately the change's (count, not required): from/to DateTime Kind refused by Npgsql; no limit on the range; the value formatted with the request culture (ar-SA) in the CSV.
Old flaws the change neither wrote nor reaches (should NOT be listed as findings, only counted):
H02 resilience POST retry; H03 notifier batch; H04 latest cache key; H07 UTC day; H08 liveness; H09 photo path traversal; H10 {@Reading} log; H11 ClockSkew; H12 error middleware; H13 Notes unbounded column; the POST /readings collectedAt Kind; ValidateScopes.
