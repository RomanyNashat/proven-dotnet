# Results: effort per agent (E4)

The bar is in `bar.md`, written before any run. Round five's fixture (`fix-labs`), the with-proven arm, the
session at medium; only the subagents' effort changes. All runs on the same harness (`1ea2e0a`, 0.3.43),
Claude Opus 5.5, graded with the hidden tests (PostgreSQL 16 here).

| Subagents | Findings | Regressions | Missed | Cost | Time |
|---|---|---|---|---|---|
| low (pilot) | 7 / 8 | 6 / 6 | K08 (clock skew) | $3.49 | 2 min |
| medium | **8 / 8** | 6 / 6 | none | $5.02 | 18 min |
| high | 7 / 8 | 6 / 6 | K08 (clock skew) | about $6.20 | stopped by the usage limit, resumed from its transcript |

- **Low loses a finding on its first run**, so by the bar every agent keeps the session's effort. The low
  run skipped the outbox as "a design change" and left the token's clock skew at the default; the medium
  run fixed both.
- **The cost is not the reason to lower it either:** low saved $1.53 on a $5 run, and lost a finding a
  reviewer would have to catch later.
- **High is no better here, and costs more.** It also missed K08, at about $6.20 against medium's $5.02.
- **K08 looks like noise more than effort:** low and high missed it and medium didn't, one run each. Round
  five's rules-only runs fixed it in all three. What one run per effort shows is "no effort is shown to
  do better than the session's medium", not a rate.
- **Decision:** agents keep taking the session's effort; none names its own. `CLAUDE_CODE_SUBAGENT_EFFORT`
  stays unset in the harness.
