using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SkillSamples.Diagnostics;

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
