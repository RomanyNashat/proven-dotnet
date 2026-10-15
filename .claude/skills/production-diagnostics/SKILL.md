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
    public string FileSystemRoot { get; set; } = "/";          // where the cgroup files are; tests use a fake
}

// One structured log line per interval with the runtime's own counters: trends without a metrics
// backend and without access to the pod. No packages: it reads what the runtime keeps, and the container's
// limits from its cgroup files, so CPU and memory are measured against what the pod may really use.
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
            var limits = ContainerLimits.Read(settings.FileSystemRoot);
            var cores = limits.CpuCores ?? Environment.ProcessorCount;
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
                    "cpu {CpuPercent:F0}% of {CpuLimitCores:0.##} cores, working set {WorkingSetMb} MB, heap {HeapMb} MB, " +
                    "fragmented {FragmentedMb} MB, memory limit {MemoryLimitMb} MB, heap limit {HeapLimitMb} MB, " +
                    "gen0/1/2 {Gen0}/{Gen1}/{Gen2}, gc pause {GcPauseMs} ms, " +
                    "lock contentions {LockContentions}, exceptions {Exceptions}",
                    ThreadPool.ThreadCount,
                    ThreadPool.PendingWorkItemCount,
                    (now.CompletedWorkItems - last.CompletedWorkItems) / seconds,
                    CpuPercent(now.Cpu - last.Cpu, TimeSpan.FromSeconds(seconds), cores),
                    cores,
                    now.WorkingSet / 1024 / 1024,
                    gc.HeapSizeBytes / 1024 / 1024,
                    gc.FragmentedBytes / 1024 / 1024,
                    (limits.MemoryBytes ?? gc.TotalAvailableMemoryBytes) / 1024 / 1024,
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

    /// <summary>CPU used as a share of what the container may use: 30 s of CPU in a minute at half a core is 100%.</summary>
    public static double CpuPercent(TimeSpan cpuUsed, TimeSpan elapsed, double cores) =>
        cpuUsed.TotalSeconds / (elapsed.TotalSeconds * cores) * 100;

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
exceptions counted. As a story: a bug starts throwing and catching 300 exceptions; that minute's line
carries the jump (each field is per interval, not a running total), and the line after the fix falls back.
On a slim image (no ICU, no tzdata) the line is written the same, with the container's memory limit.

**CPU and memory are measured against the container's limits, not the runtime's numbers.** The
runtime's own figures mislead in a pod:
- `Environment.ProcessorCount` rounds a fractional CPU limit up. With `limits.cpu: 500m` it says 1, so a
  pod using all of its half core read as **50%**, and triage called it thread starvation with the CPU
  idle. Against the limit it reads 100%, and triage says CPU-bound (tested as a story).
- `GCMemoryInfo.TotalAvailableMemoryBytes` is the GC heap limit, which in a container defaults to 75%
  of the memory limit. The line logs both: `memory limit` (the container's, what the OOM killer uses)
  and `heap limit` (the GC's).

The limits come from the cgroup files, smallest one between the container and the pod (cgroup v2), or
the container's own (v1). Without a cgroup (Windows, a machine with no limit) the line falls back to the
runtime's numbers. Tested with fake cgroup folders for v1, v2 and nested limits, and against the real
files on the CI runner:

<!-- sample: tests/SkillSamples.Tests/Diagnostics/ContainerLimits.cs -->
```csharp
    // cgroup v2: the effective limit is the smallest one on the way from the process's own group to the root.
    // Kubernetes sets one on the pod and one on the container.
    private static ContainerLimits ReadV2(string root, string cgroup)
    {
        // "0::/kubepods.slice/..." on a host; "0::/" inside a container, which sees its own group as the root.
        var own = (ReadText(Path.Combine(root, "proc", "self", "cgroup")) ?? "").Split('\n')
            .FirstOrDefault(l => l.StartsWith("0::", StringComparison.Ordinal))?[3..].Trim('/') ?? "";
        double? cpu = null;
        long? memory = null;
        for (var dir = Path.Combine(cgroup, own); ; dir = Path.GetDirectoryName(dir)!)
        {
            if (ReadText(Path.Combine(dir, "cpu.max")) is { } cpuMax && ParseCpuMax(cpuMax) is { } cores)
                cpu = Math.Min(cpu ?? double.MaxValue, cores);
            if (ReadText(Path.Combine(dir, "memory.max")) is { } memoryMax && memoryMax != "max")
                memory = Math.Min(memory ?? long.MaxValue, long.Parse(memoryMax, CultureInfo.InvariantCulture));
            if (Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(cgroup).TrimEnd(Path.DirectorySeparatorChar))
                break;
        }

        return new ContainerLimits(cpu, memory);
    }
```

The whole reader, with v1 and the parsing, is `ContainerLimits.cs` beside the heartbeat. Where an
OpenTelemetry pipeline exists, `Microsoft.Extensions.Diagnostics.ResourceMonitoring` publishes the same
as metrics (`container.cpu.limit.utilization`, `container.memory.limit.utilization`); the heartbeat
reads the files itself so it needs no package.
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

### Start from the numbers: triage a window of heartbeat lines

Before reading code, export the heartbeat lines for the window the problem was seen in (a few minutes
before it started to now) and triage them. The result is the shape every diagnosis is reported in:
**signals** (what is out of range, with the numbers), then **hypotheses** (the likely cause, why, and
the next thing to check), most likely first.

<!-- sample: tests/SkillSamples.Tests/Diagnostics/HeartbeatTriage.cs -->
```csharp
/// <summary>One heartbeat line's fields (§2), as exported from the log store for a time window.</summary>
public sealed record HeartbeatSample(
    DateTimeOffset At, int Threads, long Queued, double CpuPercent, long WorkingSetMb, long MemoryLimitMb,
    long Gen2, long GcPauseMs, long LockContentions, long Exceptions, int IntervalSeconds = 60);

public enum SignalLevel { Warning, Critical }

/// <summary>Something measurable that is out of range, with the numbers that show it.</summary>
public sealed record Signal(string Name, SignalLevel Level, string Evidence);

/// <summary>A likely cause, why the signals point at it, and the next thing to check.</summary>
public sealed record Hypothesis(string Name, string Because, string NextStep);

public sealed record Triage(IReadOnlyList<Signal> Signals, IReadOnlyList<Hypothesis> Hypotheses);
```

`HeartbeatTriage.Assess(window, cores)` applies these rules. The thresholds are starting points for a
typical API pod; a service with its own baseline uses that instead.

| Signal | Raised when | Points at |
|---|---|---|
| `threadpool.queue` | `queued` ≥ 100 in the last two lines (critical from 1,000) | with low CPU: **starvation** (blocked threads); with high CPU: **CPU-bound** |
| `threadpool.threads` | threads rising through 3+ lines, above 4 × cores | starvation: the pool keeps adding threads that block |
| `cpu.high` | `cpu` ≥ 85% in the last two lines | CPU-bound work, or allocations if `gc pause` is high too |
| `memory.near-limit` | working set ≥ 85% of the limit (critical from 95%) | memory pressure; `OOMKilled` next (§5) |
| `memory.growing` | working set rising through 3+ lines with full (gen2) collections | something holds on to objects (a leak) |
| `gc.pause` | GC pauses ≥ 10% of the interval | heavy allocation |
| `locks.contention` | ≥ 100 contentions a second | a hot lock, or a lock held across slow work |
| `exceptions.burst` | ≥ 100 in the last line and 10 × the usual rate | a failing dependency |

No signal means the runtime is fine and the time goes elsewhere: the slow request's APM spans
(database, HTTP, cache) are next. One bad line is never a trend: the rules need two lines or a run.

Tested as a story: an API that got slow at nine with low CPU. Five lines where the queue climbs to 2,500
and threads from 8 to 60: the queue is critical with its numbers, and the first hypothesis is starvation,
with where to look (`.Result`, `.Wait()`, sync I/O, a lock across an await). Also tested: a busy pod is
CPU-bound, not starving; memory rising through full collections near the limit is pressure; one bad line
raises nothing; an exception burst points at a dependency. The same window reads the same way on a slim
image (no ICU, no tzdata).

### By symptom

| Symptom | Look at first | Confirms it | Usual causes |
|---|---|---|---|
| **Slow responses** | APM latency by endpoint; request-log elapsed by route | Slow in one dependency span (DB, HTTP, Redis) vs. everywhere | Missing index or N+1 (`dba-reviewer`), a slow downstream service, pool exhaustion (below), starvation (below) |
| **Everything slow, CPU low** | Heartbeat `threads` climbing far above the core count, CPU low | `queued` > 0 for several minutes; Redis `TimeoutException` text shows `WORKER: (Busy=…,Min=…)` with Busy > Min | **Thread-pool starvation:** `.Result`/`.Wait()`/sync I/O on request threads, sync-over-async in a library, `lock` around awaited work |
| **CPU at the limit** | Heartbeat `cpu` near 100% of `cpu limit` cores; APM latency up on every endpoint | The container's `cpu.stat`: `nr_throttled` rising between two reads (ask for it, §6.1) | A limit too small for the load (fractional limits throttle in bursts), or a hot path; Server GC on a pod with one core or less |
| **High CPU** | Heartbeat `cpu`, APM by endpoint | High `gen0/1/2` and `gc pause` → allocations; otherwise hot code | Big allocations per request, regex without timeout or cache, JSON (de)serializing huge payloads, a tight retry loop |
| **Memory grows, never returns** | Heartbeat `heap` after each GC, trending up over hours | `gen2` running often while `heap` still rises | Static or singleton collections, events never unsubscribed, `IMemoryCache` without size limit or expiry, captured `HttpContext`, timers never disposed |
| **Pod restarts, no error log** | Pod status: `OOMKilled`? exit code 137? | Heartbeat `working set` approaching `memory limit` before each restart | Memory growth above, or a limit too small for the workload. **No dump is possible:** the kernel kills it outright |
| **Slower since a deploy** | APM by endpoint, before and after the deploy time | The same endpoints, the same traffic, only the version changed; heartbeat `cpu` or `gen0` stepped up at the deploy | A new N+1 or a dropped `Include` projection, a sync call added on a request path, a cache removed, a package bump that changed a query's shape (compare the SQL in APM) |
| **Slow first minute after a start** | APM latency by minute after each deploy or restart | Slow only right after start, normal once warm | JIT before tiering, EF Core building its model on the first query (compiled models), empty connection pools and caches, readiness passing before warm-up (`kubernetes-dotnet`: a startup probe, warm-up in a hosted service) |
| **Crash with a log** | The `Critical` last-words line | Exception type and stack | An exception on a background thread or in `async void`, a startup config error |
| **Errors in bursts** | Request log by status code; heartbeat `exceptions` | Same exception type and route in the burst | Downstream outage without circuit breaker (`polly-resilience`), expired secrets, connection drops |
| **413, 502, 504 or 499 from a proxy** | Which layer answered: a proxy HTML page vs the app's ProblemDetails | The `nginx` skill's playbook | Limits or timeouts that disagree between ingress, proxy and Kestrel; keep-alive mismatch; pods taking traffic while stopping |
| **Requests queue or time out under load** | HttpClient/DB timeouts in logs | Npgsql: `The connection pool has been exhausted`; SqlClient: `max pool size was reached`; APM shows time waiting for a connection | **Pool exhaustion:** connections not disposed, `new HttpClient()`, pool size × pods vs `max_connections` (`postgresql-patterns`, `sqlserver-patterns`) |

## 5. Containers and GC — the facts behind restarts

- In a container with a memory limit, .NET caps the GC heap at **75% of the limit** by default
  (the heartbeat's `heap limit`; its `memory limit` is the container's). The rest is for native memory, thread stacks
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
   For CPU throttling: `kubectl exec <pod> -- cat /sys/fs/cgroup/cpu.stat`, twice a minute apart
   (`nr_throttled` and `throttled_usec` rising means the limit is biting).
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
