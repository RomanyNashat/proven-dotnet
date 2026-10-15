# production-diagnostics — evals

Each case gives an agent one incident folder and the prompt "This service is misbehaving in production.
Here is what we have. What's happening, how sure are you, and what do we do?". Graded on the diagnosis,
the evidence it cites, the next step it asks for, and what it must not do. Run each case in a fresh
subagent with proven-dotnet and once without; grade with a separate agent that sees this file and the two
answers labelled A and B.

All cases are **held out**: the skill was not changed to pass them.

## Case 1: `starvation/`

**The answer:** thread-pool starvation, caused by sync-over-async (`.Result` twice) in the
`daily-summary` endpoint added in Monday's release.

**Must:**
- Name thread-pool starvation, from the numbers: `queued` climbing from 0 to 2400 while `threads` climb
  from 9 to 61, with CPU flat at 22–27% of 2 cores.
- Use at least one confirming signal beyond the heartbeat: every endpoint slow including `/health`, APM
  time with no span while the database and reports-service stay fast, or the Redis timeout's
  `WORKER: (Busy=58,...,Min=2)`.
- Point at the `.Result` calls in `ReportsEndpoints.cs` and fix them with `await` (async lambda,
  `GetFromJsonAsync` with the request's cancellation token).

**Must not:**
- Blame Redis, the database or reports-service (they're victims; their own numbers are normal).
- Make raising `ThreadPool.SetMinThreads` the fix. It can be a stopgap, called one.
- Ask for a memory dump.

## Case 2: `cpu-limit/`

**The answer:** the pod is CPU-bound at its limit and throttled: the PDF load needs more than half a
core. Server GC on a half-core pod makes it worse (GC pauses of 4–7 s a minute).

**Must:**
- Read `cpu` as a share of the 0.5-core limit (99%), and use `cpu.stat`: `nr_throttled` rose 581 in 600
  periods between the two reads, so almost every period was throttled.
- Say it is CPU, not thread starvation: threads stay at 6–8 while the queue grows.
- Recommend more CPU (raise the limit and request, or scale out for the letter-printing window) and
  Workstation GC (`DOTNET_gcServer=0`) on a pod this small.

**Must not:**
- Call it thread-pool starvation, or look for `.Result`.
- Treat the GC pause as a memory leak (working set is flat at about 60% of the limit).

## Case 3: `memory/`

**The answer:** a memory leak from the unbounded static `SearchCache`: every distinct query stays
forever. The kernel kills the pod at the limit (`OOMKilled`, 137), so nothing is logged and no dump is
possible.

**Must:**
- Read the trend: working set and heap up about 100 MB an hour since the release, gen2 collections
  climbing while the heap still grows, and an OOM kill every 4–5 hours.
- Name `SearchCache`'s static `ConcurrentDictionary` with no size limit or expiry as the cause, and fix
  it with a bounded cache (`IMemoryCache` with `SizeLimit` and expiry, or `HybridCache`).
- To confirm, if needed: a gcdump from one pod with approval (it pauses the service), not a full dump.

**Must not:**
- Ask for a full or heap dump (it holds patient data).
- Recommend raising the memory limit as the fix (it only spaces the kills out).
- Blame the rising CPU at the end on the code (it's the GC working harder near the limit).

## Scoring

Per case: diagnosis right (yes/no), each Must met, each Must not avoided. Report per run: diagnoses
right out of 3, Musts met out of 9, Must-nots broken out of 8. Compare with and without.
