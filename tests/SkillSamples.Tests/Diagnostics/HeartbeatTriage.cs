namespace SkillSamples.Diagnostics;

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

/// <summary>
/// Turns a window of heartbeat lines into signals and ranked hypotheses. A signal needs the numbers
/// behind it; a hypothesis needs a signal; nothing is guessed from a single line where a trend is meant.
/// The thresholds are starting points for a typical API pod: tune them to the service's own baseline.
/// </summary>
public static class HeartbeatTriage
{
    public const long QueueWarning = 100;
    public const long QueueCritical = 1_000;
    public const double CpuHigh = 85;
    public const double MemoryWarningShare = 0.85;
    public const double MemoryCriticalShare = 0.95;
    public const double GcPauseShare = 0.10;
    public const double LockContentionsPerSecond = 100;
    public const long ExceptionBurstMinimum = 100;

    public static Triage Assess(IReadOnlyList<HeartbeatSample> window, int cores)
    {
        if (window.Count == 0)
        {
            return new([], []);
        }

        var signals = new List<Signal>();
        var last = window[^1];

        var queued = window.TakeLast(2).Select(s => s.Queued).ToList();
        if (queued.Count == 2 && queued.Min() >= QueueWarning)
        {
            signals.Add(new("threadpool.queue", queued.Min() >= QueueCritical ? SignalLevel.Critical : SignalLevel.Warning,
                $"queued {string.Join(" -> ", queued)} work items in the last two lines (warning from {QueueWarning})"));
        }

        if (window.Count >= 3 && Rising(window.Select(s => (double)s.Threads)) && last.Threads > cores * 4)
        {
            signals.Add(new("threadpool.threads", SignalLevel.Warning,
                $"threads rose {window[0].Threads} -> {last.Threads} on {cores} cores"));
        }

        var cpu = window.TakeLast(2).Select(s => s.CpuPercent).ToList();
        var cpuHigh = cpu.Count == 2 && cpu.Min() >= CpuHigh;
        if (cpuHigh)
        {
            signals.Add(new("cpu.high", SignalLevel.Warning, $"cpu {string.Join("% -> ", cpu.Select(c => c.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)))}% (from {CpuHigh}%)"));
        }

        if (last.MemoryLimitMb > 0)
        {
            var share = (double)last.WorkingSetMb / last.MemoryLimitMb;
            if (share >= MemoryWarningShare)
            {
                signals.Add(new("memory.near-limit", share >= MemoryCriticalShare ? SignalLevel.Critical : SignalLevel.Warning,
                    $"working set {last.WorkingSetMb} MB of a {last.MemoryLimitMb} MB limit ({share:P0})"));
            }
        }

        if (window.Count >= 3 && Rising(window.Select(s => (double)s.WorkingSetMb)) && window.Any(s => s.Gen2 > 0))
        {
            signals.Add(new("memory.growing", SignalLevel.Warning,
                $"working set rose {window[0].WorkingSetMb} -> {last.WorkingSetMb} MB through {window.Sum(s => s.Gen2)} gen2 collections"));
        }

        if (last.GcPauseMs >= last.IntervalSeconds * 1000 * GcPauseShare)
        {
            signals.Add(new("gc.pause", SignalLevel.Warning, $"gc paused {last.GcPauseMs} ms in {last.IntervalSeconds} s"));
        }

        if (last.LockContentions >= last.IntervalSeconds * LockContentionsPerSecond)
        {
            signals.Add(new("locks.contention", SignalLevel.Warning, $"{last.LockContentions} lock contentions in {last.IntervalSeconds} s"));
        }

        if (window.Count >= 2)
        {
            var before = Median(window.SkipLast(1).Select(s => s.Exceptions));
            if (last.Exceptions >= ExceptionBurstMinimum && last.Exceptions >= 10 * Math.Max(before, 1))
            {
                signals.Add(new("exceptions.burst", SignalLevel.Warning, $"exceptions {before:F0} per line before, {last.Exceptions} now"));
            }
        }

        return new(signals, Hypotheses(signals, cpuHigh));
    }

    private static List<Hypothesis> Hypotheses(List<Signal> signals, bool cpuHigh)
    {
        bool Has(string name) => signals.Any(s => s.Name == name);
        var result = new List<Hypothesis>();

        if (Has("threadpool.queue") && !cpuHigh)
        {
            result.Add(new("threadpool.starvation",
                "work is queuing while the CPU is not busy: threads are blocked, not working",
                "look for .Result, .Wait(), sync I/O or a lock held across an await on request paths; ask for live counters (§6.2)"));
        }

        if (Has("threadpool.queue") && cpuHigh)
        {
            result.Add(new("cpu.bound", "work is queuing because every core is busy",
                "find the hot endpoints in APM; high gen0/gc pause points at allocations"));
        }

        if (Has("memory.near-limit") || Has("memory.growing"))
        {
            result.Add(new("memory.pressure",
                Has("memory.growing") ? "memory keeps rising through full collections: something holds on to objects" : "the pod is close to its memory limit",
                "check for OOMKilled restarts (§6.1); a gcdump, with approval, shows what holds the memory (§6.4)"));
        }

        if (Has("locks.contention"))
        {
            result.Add(new("lock.contention", "threads wait on each other's locks",
                "find a lock on a hot path or around slow work; the live counters show the contention rate"));
        }

        if (Has("exceptions.burst"))
        {
            result.Add(new("failing.dependency", "exceptions jumped in one interval",
                "group that minute's errors by type and route; a downstream outage without a circuit breaker is the usual cause"));
        }

        if (Has("gc.pause") && !Has("memory.growing"))
        {
            result.Add(new("allocation.heavy", "the GC is pausing the service for a large share of the time",
                "find the endpoints that allocate most (large payloads, buffers, string building)"));
        }

        if (result.Count == 0)
        {
            result.Add(new("runtime.healthy", "the runtime shows nothing out of range",
                "the time is spent elsewhere: look at the slow request's APM spans (database, HTTP, cache)"));
        }

        return result;
    }

    private static bool Rising(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Zip(list.Skip(1)).All(p => p.Second > p.First);
    }

    private static double Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
    }
}
