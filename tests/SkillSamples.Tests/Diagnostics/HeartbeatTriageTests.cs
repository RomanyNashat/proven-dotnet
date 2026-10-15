using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Diagnostics;

public sealed class HeartbeatTriageTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static HeartbeatSample Line(int minute, int threads = 8, long queued = 0, double cpu = 20, long workingSetMb = 300,
        long gen2 = 0, long gcPauseMs = 50, long locks = 10, long exceptions = 5) =>
        new(Start.AddMinutes(minute), threads, queued, cpu, workingSetMb, MemoryLimitMb: 1024, gen2, gcPauseMs, locks, exceptions);

    private static string[] Names(IEnumerable<Signal> signals) => [.. signals.Select(s => s.Name)];

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_TheApiGetsSlowAtNineWithLowCpu_TheHeartbeatsPointAtBlockedThreads_NotAtTheDatabase()
    {
        // Given: five heartbeat lines from the pod the morning it got slow: work queues up and threads keep
        // being added, but the CPU stays low
        HeartbeatSample[] window =
        [
            Line(0, threads: 8, queued: 0),
            Line(1, threads: 14, queued: 40),
            Line(2, threads: 25, queued: 300),
            Line(3, threads: 41, queued: 1_200),
            Line(4, threads: 60, queued: 2_500),
        ];

        // When: the window is triaged
        var triage = HeartbeatTriage.Assess(window, cores: 4);

        // Then: the queue is critical with its numbers, the thread count is climbing, and the first cause
        // to check is blocking on async code, with where to look
        var queue = Assert.Single(triage.Signals, s => s.Name == "threadpool.queue");
        Assert.Equal(SignalLevel.Critical, queue.Level);
        Assert.Contains("1200 -> 2500", queue.Evidence);
        Assert.Contains("threadpool.threads", Names(triage.Signals));
        Assert.Equal("threadpool.starvation", triage.Hypotheses[0].Name);
        Assert.Contains(".Result", triage.Hypotheses[0].NextStep);
    }

    [Fact]
    public void Healthy_NoSignals_AndThePointerGoesToTheSpans()
    {
        var triage = HeartbeatTriage.Assess([Line(0), Line(1), Line(2)], cores: 4);

        Assert.Empty(triage.Signals);
        Assert.Equal("runtime.healthy", Assert.Single(triage.Hypotheses).Name);
    }

    [Fact]
    public void BusyCores_QueueWithHighCpu_IsCpuBound_NotStarvation()
    {
        var triage = HeartbeatTriage.Assess([Line(0, cpu: 90, queued: 200), Line(1, cpu: 95, queued: 400)], cores: 4);

        Assert.Equal(new[] { "cpu.bound" }, triage.Hypotheses.Select(h => h.Name).ToArray());
    }

    [Fact]
    public void MemoryRisingThroughFullCollections_NearTheLimit_IsMemoryPressure()
    {
        var triage = HeartbeatTriage.Assess(
            [Line(0, workingSetMb: 600, gen2: 1), Line(1, workingSetMb: 760, gen2: 2), Line(2, workingSetMb: 990, gen2: 3)], cores: 4);

        Assert.Equal(new[] { "memory.near-limit", "memory.growing" }, Names(triage.Signals));
        Assert.Equal(SignalLevel.Critical, triage.Signals[0].Level);   // 990 of 1024 is over 95%
        Assert.Equal("memory.pressure", Assert.Single(triage.Hypotheses).Name);
    }

    [Fact]
    public void OneBadLine_IsNotATrend()
    {
        // A single queued spike, or memory up in one line, isn't reported: the rules need two lines or a run.
        var triage = HeartbeatTriage.Assess([Line(0), Line(1, queued: 5_000, workingSetMb: 500)], cores: 4);

        Assert.Empty(triage.Signals);
    }

    [Fact]
    public void ExceptionBurst_AgainstTheUsualRate_IsAFailingDependency()
    {
        var triage = HeartbeatTriage.Assess([Line(0, exceptions: 4), Line(1, exceptions: 6), Line(2, exceptions: 900)], cores: 4);

        Assert.Equal("exceptions.burst", Assert.Single(triage.Signals).Name);
        Assert.Equal("failing.dependency", Assert.Single(triage.Hypotheses).Name);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoIcuNoTzdata_TheTriageReadsTheSameWindowTheSameWay()
    {
        ProductionConditions.Require();

        var triage = HeartbeatTriage.Assess([Line(0, cpu: 91.4, queued: 150), Line(1, cpu: 96.6, queued: 180)], cores: 2);

        Assert.Equal("cpu.bound", Assert.Single(triage.Hypotheses).Name);
        Assert.Contains("91% -> 97%", Assert.Single(triage.Signals, s => s.Name == "cpu.high").Evidence);
    }
}
