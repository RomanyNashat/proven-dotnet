# Eval results: production-diagnostics

## 2026-10-15 — first runs (proven-dotnet 0.2.13)

Held out: `production-diagnostics` was not changed before these runs. One run per case each way, so these
are results, not rates.

**How it ran.** Each run read a copy of one incident folder outside the repo, without `evals.md`.
- **With proven:** a general-purpose agent told to read `production-diagnostics` first and follow it,
  and any skill it points to.
- **Baseline:** the same prompt, told not to use skills or read anything outside the incident folder. It
  was started from the same session, so it **had the house rules and `core.md` in context**: rules only.
- **Grading:** a separate agent that hadn't seen the runs made, given the answer key and the two answers
  per case as "A" and "B". The letters were shuffled per case. Every verdict quotes the answer.

| | With proven | Baseline (rules only) |
|---|---|---|
| Diagnoses right | **3 / 3** | **3 / 3** |
| Musts met (partial = 0.5) | **9 / 9** | **8.5 / 9** |
| Must-nots broken | 0 / 8 | 0 / 8 |
| Other errors about the data | 0 | 2 (minor, memory case) |
| Tokens (three runs) | 386k | 344k |

**Where they differed:** only the memory case.
- The baseline asked for a gcdump and ruled out a full dump, but didn't say the gcdump pauses the service
  or needs approval.
- It read `CompletedPerSecond` as requests a second, and gave gen2 counts "an hour", a unit the export
  doesn't have.

**Both** named starvation from the queue and thread numbers with CPU flat, and confirmed it beyond the
heartbeat (`/health` slow, the APM gap, the Redis timeout's worker counts). Both read the CPU against
the half-core limit, used the throttling counters, and named Server GC on a small pod as what makes it
worse. Both found the unbounded static cache and fixed it with a bounded one. Neither blamed a victim, made `SetMinThreads` or a bigger limit the fix, or asked for a full
dump.

## What it means

- **These cases are within what the rules and a strong model already know.** Thread-pool starvation from
  `.Result`, CPU throttling and an unbounded static cache are the classic three, and the rules ban the
  first by name. The skill's gain here is care with the evidence (what the numbers measure, what a
  gcdump costs), not the diagnosis.
- **The baseline's two slips are the kind the skill exists for:** reading a counter as something it
  isn't, and asking for a step on a production pod without saying what it costs. One run each, so this is
  a direction, not a rate.
- **Next:** a case the rules don't cover (a sync-over-async that only shows under a CPU limit, or a
  socket exhaustion with flat thread numbers), and a baseline from a session outside the repo.
