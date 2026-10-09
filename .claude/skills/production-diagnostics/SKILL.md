---
name: production-diagnostics
description: Diagnose a slow, leaking, crashing or thread-starved .NET service in production from logs and APM first — a tested runtime heartbeat log line, a symptom playbook, container GC facts, safe requests to whoever runs the cluster, no memory dumps by default.
---

# Production diagnostics

The service is slow, its memory keeps growing, it restarts, or it errors in bursts. This skill turns a
symptom into evidence and a fix.

**Start from how your team can actually see production**, not from tool tutorials. Often:
- **Developers see logs and an APM tool** (Elastic, Application Insights, Datadog, Grafana with
  OpenTelemetry), and not the pods.
- **Only the platform team (SRE, ops) can reach the pods** (`kubectl exec`, `cp`, `debug`). Anything
  deeper than logs is a written request to them (§6).
- **No memory dumps by default.** A full or heap dump is a copy of the process memory: user data,
  tokens, connection strings. Tested on .NET 10: a string that was in memory at the time is inside a
  heap dump; it isn't inside a triage dump or the crash report.

So the job is mostly: **make the service tell you enough in its logs**, then read them well. If your team
has direct access to pods and a metrics backend, the same steps apply with fewer hops.

## 1. What Claude may and may not read

| OK to paste or analyze | Never |
|---|---|
| The heartbeat lines (§2), APM charts or numbers, counters output | Raw production log lines that may hold request or response bodies |
| Log fields: time, level, message template, route, status code, elapsed, exception **type** and stack | Personal or health data, tokens, connection strings |
| Pod status and events (`OOMKilled`, restart count, exit code) | Any full or heap dump, or commands that print values from one (`dumpobj` on strings, `clrstack -a`) |
| A crash report (`*.crashreport.json`: exception type and stack, no message) | Uploading a dump or a log export anywhere outside your organisation |

Before sharing logs with Claude, export only the fields above. Summaries go in the journal, never the data.

## 2. Make the service diagnosable

### Runtime heartbeat — metrics through the logs

One structured line per minute with the runtime's own numbers. It reaches your log store with the other
logs, so you get trends with no metrics backend and no access to the pod. It only reads counters the
runtime already keeps, so it needs no packages and costs one log line a minute.

<!-- sample: tests/SkillSamples.Tests/Diagnostics/RuntimeHeartbeat.cs -->
```csharp
public sealed class RuntimeHeartbeatOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
    public long ThreadPoolQueueWarning { get; set; } = 100;   // queued work above this → Warning
}

// One structured log line per interval with the runtime's own counters: trends without a metrics
// backend and without access to the pod. No packages; it only reads what the runtime already keeps.
public sealed class RuntimeHeartbeat(ILogger<RuntimeHeartbeat> logger, IOptions<RuntimeHeartbeatOptions> options, TimeProvider time)
    : BackgroundService
{
    private long _exceptions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> count =
            (_, _) => Interlocked.Increment(ref _exceptions);
        AppDomain.CurrentDomain.FirstChanceException += count;
        try
        {
            using var process = Process.GetCurrentProcess();
            var last = Snapshot.Take(process, time, Interlocked.Read(ref _exceptions));
            using var timer = new PeriodicTimer(settings.Interval, time);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                process.Refresh();
                var now = Snapshot.Take(process, time, Interlocked.Read(ref _exceptions));
                var seconds = Math.Max((now.At - last.At).TotalSeconds, 1);
                var gc = GC.GetGCMemoryInfo();

                var level = ThreadPool.PendingWorkItemCount > settings.ThreadPoolQueueWarning ? LogLevel.Warning : LogLevel.Information;
                logger.Log(level,
                    "Runtime heartbeat: threads {ThreadPoolThreads}, queued {ThreadPoolQueue}, completed/s {WorkItemsPerSecond:F0}, " +
                    "cpu {CpuPercent:F0}%, working set {WorkingSetMb} MB, heap {HeapMb} MB, fragmented {FragmentedMb} MB, " +
                    "memory limit {MemoryLimitMb} MB, gen0/1/2 {Gen0}/{Gen1}/{Gen2}, gc pause {GcPauseMs} ms, " +
                    "lock contentions {LockContentions}, exceptions {Exceptions}",
                    ThreadPool.ThreadCount,
                    ThreadPool.PendingWorkItemCount,
                    (now.CompletedWorkItems - last.CompletedWorkItems) / seconds,
                    (now.Cpu - last.Cpu).TotalSeconds / (seconds * Environment.ProcessorCount) * 100,
                    now.WorkingSet / 1024 / 1024,
                    gc.HeapSizeBytes / 1024 / 1024,
                    gc.FragmentedBytes / 1024 / 1024,
                    gc.TotalAvailableMemoryBytes / 1024 / 1024,
                    now.Gen0 - last.Gen0, now.Gen1 - last.Gen1, now.Gen2 - last.Gen2,
                    (long)(now.GcPause - last.GcPause).TotalMilliseconds,
                    now.LockContentions - last.LockContentions,
                    now.Exceptions - last.Exceptions);

                last = now;
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= count;
        }
    }

    private readonly record struct Snapshot(
        DateTimeOffset At, long CompletedWorkItems, TimeSpan Cpu, long WorkingSet,
        int Gen0, int Gen1, int Gen2, TimeSpan GcPause, long LockContentions, long Exceptions)
    {
        public static Snapshot Take(Process process, TimeProvider time, long exceptions) => new(
            time.GetUtcNow(), ThreadPool.CompletedWorkItemCount, process.TotalProcessorTime, process.WorkingSet64,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration(),
            Monitor.LockContentionCount, exceptions);
    }
}
```

```csharp
// Program.cs
builder.Services.AddSingleton(TimeProvider.System);   // if not registered already
builder.Services.Configure<RuntimeHeartbeatOptions>(builder.Configuration.GetSection("RuntimeHeartbeat"));
builder.Services.AddHostedService<RuntimeHeartbeat>();
```

Tested: a line per interval with every field, a warning when the queue passes the threshold, and handled
exceptions counted.
- `heap` is as of the last GC, so it reads 0 until the first one.
- `exceptions` counts every thrown exception, handled ones included. A steady high number means
  exceptions are used for control flow; a jump means something started failing.
- Search it by the message template (`Runtime heartbeat*`) and chart a field over time.
- Where a metrics backend exists, `System.Runtime` metrics through OpenTelemetry give the same numbers.
  The heartbeat is for when the logs are all you have.

### Last words: log the crash before the process ends

If your logger writes asynchronously (Serilog's async sink, a buffered provider), the last lines can still
be in the buffer when the process dies. Log the unhandled exception and flush:

```csharp
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    app.Logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception, process terminating: {IsTerminating}", e.IsTerminating);
    Serilog.Log.CloseAndFlush();   // only if the service uses Serilog's static Log; otherwise dispose the logger provider
};
```

### Request logs and correlation

Slow-endpoint questions start from one log line per request with its route, status and elapsed time,
and a correlation or trace ID that ties one request's lines together across services. If the service
doesn't have them, add them first (Serilog or OpenTelemetry logging with the trace ID on every line).

## 3. Is the APM actually working?

Teams often skip metrics because "the APM covers it". Check that it does before relying on it:
1. **In the APM's service list:** is this service there, with transactions in the last hour? If not, it
   isn't reporting, whatever the config says.
2. **In the repo:** the vendor's package or OpenTelemetry with an exporter. With neither, it depends on
   profiler-based auto-instrumentation set up by the platform, and only the platform team can confirm it.
3. **One setting quietly disables profiler-based auto-instrumentation:** `DOTNET_EnableDiagnostics=0`
   (or `DOTNET_EnableDiagnostics_Profiler=0`). Ask whether either is set on the pod. The same switch also
   blocks every `dotnet-*` tool.
4. **Alpine images use musl.** Check that the agent you use supports musl; some profilers document glibc
   only, and then they silently don't load.
5. If it works: request latency per endpoint, error rate, and the trace of one slow request across
   services are there. Use them before anything else.

## 4. Symptom playbook

| Symptom | Look at first | Confirms it | Usual causes |
|---|---|---|---|
| **Slow responses** | APM latency by endpoint; request-log elapsed by route | Slow in one dependency span (DB, HTTP, Redis) vs. everywhere | Missing index or N+1 (`dba-reviewer`), a slow downstream service, pool exhaustion (below), starvation (below) |
| **Everything slow, CPU low** | Heartbeat `threads` climbing far above the core count, CPU low | `queued` > 0 for several minutes; Redis `TimeoutException` text shows `WORKER: (Busy=…,Min=…)` with Busy > Min | **Thread-pool starvation:** `.Result`/`.Wait()`/sync I/O on request threads, sync-over-async in a library, `lock` around awaited work |
| **High CPU** | Heartbeat `cpu`, APM by endpoint | High `gen0/1/2` and `gc pause` → allocations; otherwise hot code | Big allocations per request, regex without timeout or cache, JSON (de)serializing huge payloads, a tight retry loop |
| **Memory grows, never returns** | Heartbeat `heap` after each GC, trending up over hours | `gen2` running often while `heap` still rises | Static or singleton collections, events never unsubscribed, `IMemoryCache` without size limit or expiry, captured `HttpContext`, timers never disposed |
| **Pod restarts, no error log** | Pod status: `OOMKilled`? exit code 137? | Heartbeat `working set` approaching `memory limit` before each restart | Memory growth above, or a limit too small for the workload. **No dump is possible:** the kernel kills it outright |
| **Crash with a log** | The `Critical` last-words line | Exception type and stack | An exception on a background thread or in `async void`, a startup config error |
| **Errors in bursts** | Request log by status code; heartbeat `exceptions` | Same exception type and route in the burst | Downstream outage without circuit breaker (`polly-resilience`), expired secrets, connection drops |
| **413, 502, 504 or 499 from a proxy** | Which layer answered: a proxy HTML page vs the app's ProblemDetails | The `nginx` skill's playbook | Limits or timeouts that disagree between ingress, proxy and Kestrel; keep-alive mismatch; pods taking traffic while stopping |
| **Requests queue or time out under load** | HttpClient/DB timeouts in logs | Npgsql: `The connection pool has been exhausted`; SqlClient: `max pool size was reached`; APM shows time waiting for a connection | **Pool exhaustion:** connections not disposed, `new HttpClient()`, pool size × pods vs `max_connections` (`postgresql-patterns`, `sqlserver-patterns`) |

## 5. Containers and GC — the facts behind restarts

- In a container with a memory limit, .NET caps the GC heap at **75% of the limit** by default
  (`memory limit` in the heartbeat shows what it sees). The rest is for native memory, thread stacks
  and the runtime.
- ASP.NET Core uses **Server GC**. On .NET 9+ it adapts its heap count to the load (DATAS, on by
  default), so a small pod doesn't reserve one heap per core.
- An `OutOfMemoryException` in the logs means the managed heap hit its cap. `OOMKilled` in the pod
  status means the whole process passed the container limit and the kernel killed it, with no log and
  no dump. They have different fixes: the first is managed memory, the second is total memory or the
  limit.

## 6. Asking the platform team — safe requests only

When logs and APM aren't enough, write the request. Ask only for **safe** artifacts, in this order:

1. **Pod facts:** `kubectl describe pod` (events, last state, `OOMKilled`, restart count, exit code,
   limits) and the container's logs around the restart (`kubectl logs --previous`).
2. **Live counters** (no data, no pause, no special permission). Runtime images usually have no tools:
   download the single-file tool on a machine (`https://aka.ms/dotnet-counters/linux-x64`, or
   `linux-musl-x64` for Alpine), then:
   ```bash
   kubectl cp ./dotnet-counters <pod>:/tmp/dotnet-counters -c <container>
   kubectl exec <pod> -c <container> -- /tmp/dotnet-counters collect -p 1 --duration 00:00:02:00 \
       --counters System.Runtime,Microsoft.AspNetCore.Hosting,System.Net.Http --format csv -o /tmp/counters.csv
   kubectl cp <pod>:/tmp/counters.csv ./counters.csv -c <container>
   ```
   `-p 1` assumes the app is the container's main process (check with `ps` if not). This needs `tar` in
   the container (for `kubectl cp`) and the same user as the app, which `exec` gives by default.
3. **Crash report on the next crash.** Environment on the deployment:
   `DOTNET_DbgEnableMiniDump=1`, `DOTNET_DbgMiniDumpType=3` (triage), `DOTNET_EnableCrashReport=1`,
   `DOTNET_DbgMiniDumpName=/tmp/crash-%p.dmp`. **Ask for the `.crashreport.json` only**; it holds the
   exception type and stack. Writing the dump needs the ptrace capability in the container.
4. **gcdump, with explicit approval:** object types, counts, sizes and who holds them, but no field
   values, which is the right tool for a leak. It **forces a full GC that pauses the service**, so take
   it from one pod, out of peak.
5. **Full or heap dump: only with security approval.** It holds whatever was in memory. It stays inside
   the cluster, is analyzed there, and is deleted after. Never copy it to a laptop or a chat.

The request text should say: the symptom, the pod or time window, exactly which of the above, and what
to send back.

## 7. Fix, then prove it

- Fix in code with a test that reproduces the cause where possible: a characterization test around the
  blocking call, or a test that the cache has a size limit.
- After the deploy, compare the same heartbeat fields or APM chart before and after, over the same time
  of day. "Looks better" isn't proof; the numbers are.
- When the cause only shows under load (starvation, pool exhaustion), reproduce it before the fix and
  confirm it after with a load test in a test environment: see `k6-load-testing`.
- Write the cause and the evidence in the journal and, if it was a design flaw, a lightweight ADR.
