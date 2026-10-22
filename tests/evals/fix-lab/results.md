# Eval results: the fix eval, round three

## 2026-10-22: a new service, three runs per arm (proven-dotnet 0.2.20)

Held out: `HomeReadings` was written after the skills, and nothing in the harness was changed for it. Each
run fixed its own copy outside the repo, with `task.md` word for word; builds were allowed, PostgreSQL
and Docker weren't there. The copies are in `runs/` as the runs left them, and `fix-evals.yml` grades them.

| | With proven | Rules only | No rules |
|---|---|---|---|
| **Findings fixed** (3 runs × 13) | **39 / 39** | **39 / 39** | **33 / 39** |
| Regressions kept (3 runs × 6) | 18 / 18 | 18 / 18 | 18 / 18 |
| Builds | 3 / 3 | 3 / 3 | 3 / 3 |
| Missed | none | none | H11 and H13, in all three runs |
| Its own unit tests | 84, 82, 82 | 47, 56, 44 | 12, 0, 0 |
| Work per run | about 550k tokens (3 reviewers about 100k each, the fixer about 230k); 22 min | about 175k; 6 to 7 min | 630k to 940k with cache reads, $0.45 to $1.16; 3 to 4 min |

Per finding, every run of an arm gave the same result. `scores.json` holds the counts the eval gate
compares against.

## What it means

- **This fixture doesn't separate the skills from the rules.** Rules only fixed all 13 in every run,
  including the five only the skills teach: `Kind=Local` refused by Npgsql (H01), the resilience handler
  retrying POSTs (H02), one bad alert stopping the batch (H03), the UTC day (H07) and liveness checking
  the database (H08). A strong model with a compiler knows these. So the skills cost about three times
  the work here and changed no score.
- **The rules carry the house numbers.** No rules missed exactly the two findings that are house
  decisions rather than knowledge: a 30-second clock skew (H11) and a length on every string column
  (H13). Both were missed in all three runs, and nothing else was.
- **No spread between runs this time.** Round two swung by four findings between single runs. Here each
  arm gave the same result three times. That's evidence the round-two spread came from that fixture and
  its contract, not from this one. It isn't proof that one run is enough on the next fixture.
- **The reviewers found more than the tests measure.** All three proven runs, and the rules-only runs,
  flagged the national ID taken from the body, mg/dL readings compared with a mmol/L threshold, `HI`/`LO`
  meter text that would lose the most urgent reading, and duplicate alerts from more than one pod. None
  is graded. They're real, so they're worth adding to the next fixture's tests.
- **A gap in the skills, named by a reviewer:** no skill covers file uploads (client file names, path
  traversal, checking the bytes, size limits). Only `rules/security.md` mentions them. Every arm fixed H09
  anyway.

## What the gate gets from this

Two arms are at the ceiling, so this baseline can catch harm, not gains: a harness change that makes a
with-proven run miss something it fixed before fails the PR. To show a change helps, the next fixture
needs findings a strong model gets wrong on its own: what the skills' own tests found in real libraries
(round one's `HttpClient` logging the exception text, `UseSerilog()` beside `EnableRedaction()`), not
what a careful reviewer already knows.

## Disclosures

- **The usage limit stopped most runs midway.** At about 17:50, twelve of the fourteen agents and two of
  the three `claude -p` runs hit the account's session limit. After the reset each was resumed from its
  own transcript with "carry on from where you stopped". The token counts above for the agents are
  what they used after resuming, so they're low.
- **H03 changed after the runs, before grading.** It used one patient with two national IDs to tell the
  two alerts apart. All three proven fixers refuse a national ID that differs from the patient's earlier
  readings, a stopgap for the national ID the reviewers said shouldn't come from the body. It now uses two patients. No other
  test changed, both references still pass, and the original still fails it.
- **One fixer ran PostgreSQL.** The task said there's none. The with-proven-2 fixer found the server
  binaries installed and ran its own throwaway cluster to check its notifier. That helped it catch a bug
  in its first fix. Its score is the same as the other two proven runs, which didn't.
- **One fixer wrote outside its folder.** The with-proven-3 fixer's first failing photo test ran the
  original path-traversal code, which wrote two small files under `/tmp`. It deleted them and moved its
  tests' output into their own folder.
- The reviewers had no Roslyn MCP and read the files directly. The reports were moved from their
  transcripts into the run folders by a script, and a fresh `tdd-guide` merged them, so the session that
  wrote the answer key didn't.
