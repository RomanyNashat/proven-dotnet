# Results: effort per agent (E4)

The bar is in `bar.md`, written before any run. Round five's fixture (`fix-labs`), the with-proven arm, the
session at medium; only the subagents' effort changes. All runs on the same harness (`1ea2e0a`, 0.3.43),
Claude Opus 5.5, graded with the hidden tests (PostgreSQL 16 here).

| Subagents | Findings | Regressions | Missed | Cost | Time |
|---|---|---|---|---|---|
| low (pilot) | 7 / 8 | 6 / 6 | K08 (clock skew) | $3.49 | 2 min |
| medium | **8 / 8** | 6 / 6 | none | $5.02 | 18 min |
| high | (stopped on the usage limit; to be rerun) | | | | |

- **Low loses a finding on its first run**, so by the bar every agent keeps the session's effort. The low
  run skipped the outbox as "a design change" and left the token's clock skew at the default; the medium
  run fixed both.
- **The cost is not the reason to lower it either:** low saved $1.53 on a $5 run, and lost a finding a
  reviewer would have to catch later.
- One run per effort: "lost a finding once" is enough to stop by the bar, not a measured rate.
