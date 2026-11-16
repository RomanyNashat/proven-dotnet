# Results

## Pilot, 2026-11-16 (claude-opus-5-5, default effort, one run per request per arm)

| | flat (83 entries, 12,328 chars) | hubs (29 entries, 3,990 chars) |
|---|---|---|
| Right skill opened | 17 of 18 | 17 of 18 |
| A hub, then a member | - | 17 of 18 |
| Turns per request, on average | 3.0 | 3.9 |

**The bar is met:** routing is the same (17 against 17, the bar allowed one fewer), the hop was followed 17
times (the bar asked for 16), and it cost 0.9 of a turn (the bar allowed 1).

**The one miss was the same in both arms:** "grade the codebase's health A to F" opened the `/health-check`
command, which runs `code-health` itself. It's the command, not a wrong skill. The requests come from
`tests/routing/requests.tsv`, written against the skills' descriptions, so the pilot says the hop works.
It doesn't say the hubs route as well as the flat listing on requests nobody tuned.

**Per run:** `runs/pilot/grade.txt`. The raw transcripts aren't committed: they repeat each skill's text,
and `grade.py` reproduces the table from them.

**Next, per the bar:** the grid, which is every request in `tests/routing/requests.tsv`, three runs each
plus a noise floor, before anything in the listing moves. That's 128 × 3 × 2 = 768 sessions.
