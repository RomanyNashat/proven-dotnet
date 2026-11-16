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

## Round one, 2026-11-16: every routing request, once each

| | flat | hubs |
|---|---|---|
| Right skill opened | 127 of 128 | 124 of 128 |
| A hub, then one of its members | - | 107 of 107 that took a hub |
| Turns per request, on average | 3.0 | 3.8 |

The hub runs hit the 5-hour usage limit at run 36 and were resumed after it reset (`RESUME=1`). The grader
counts a command that runs the wanted skill as right in both arms (`/coverage` runs `test-coverage`). The
hub named `stories` shared its name with the `/stories` command; it's `story-work` now, and `build.py`
refuses a hub named like a command or a skill. The pilot's one stories run went through the old name.

**Both arms missed one:** "review the story map" opened `story-check`, not `story-review`.

## The three requests only the hubs missed, three more times each

| Request | flat | hubs |
|---|---|---|
| read the connection string from Key Vault in App Service (`azure-deployment`) | 3 of 3 | 0 of 3: `apis-and-security` → `secret-management` every time |
| client deadline for a gRPC call between two services (`grpc-patterns`) | 3 of 3 | 2 of 3 (once `design-and-architecture` → `polly-resilience`) |
| write up for the mobile developers what changed in our endpoints… (`api-contract-evolution`) | 3 of 3 | 2 of 3 (once `writing` → `simplicity`) |

**The reading:** one systematic miss, a request that sits between two hubs, and two that the hop makes
noisy. The hubs are viable but not free. The decision is in `docs/decisions/skill-listing-budget.md`: the
flat listing stays while it fits, and the hubs are the way out after that, with a skill two hubs could
own listed in both.

Per run: `runs/round1/grade.txt`, `runs/repeat1..3/grade.txt`.
