using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Diagnostics;

/// <summary>A folder laid out like a container's /sys/fs/cgroup and /proc/self/cgroup, with chosen limits.</summary>
public sealed class FakeCgroup : IDisposable
{
    public FakeCgroup() => Root = Directory.CreateTempSubdirectory("cgroup-").FullName;

    public string Root { get; }

    public FakeCgroup V2(string ownGroup = "/", string? cpuMax = null, string? memoryMax = null)
    {
        Write("sys/fs/cgroup/cgroup.controllers", "cpu memory");
        Write("proc/self/cgroup", $"0::{ownGroup}\n");
        return Limits(ownGroup, cpuMax, memoryMax);
    }

    /// <summary>Limits on one group, e.g. the pod's, above the container's own.</summary>
    public FakeCgroup Limits(string group, string? cpuMax = null, string? memoryMax = null)
    {
        var dir = "sys/fs/cgroup/" + group.Trim('/');
        if (cpuMax is not null) Write($"{dir}/cpu.max", cpuMax);
        if (memoryMax is not null) Write($"{dir}/memory.max", memoryMax);
        return this;
    }

    public FakeCgroup V1(string quota, string period, string memoryLimit)
    {
        Write("sys/fs/cgroup/cpu/cpu.cfs_quota_us", quota);
        Write("sys/fs/cgroup/cpu/cpu.cfs_period_us", period);
        Write("sys/fs/cgroup/memory/memory.limit_in_bytes", memoryLimit);
        return this;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text + "\n");
    }
}

[Collection("heartbeat")]
public sealed class ContainerLimitsTests
{
    private const long Mb = 1024 * 1024;

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_APodLimitedToHalfACoreRunsFlatOut_TheHeartbeatSaysSo_AndTriageFindsTheCpu()
    {
        // Given: a pod with `limits.cpu: 500m` that uses all of it, 30 CPU-seconds every minute, two minutes
        // running, with work queuing up. The runtime reports 1 processor: it rounds half a core up
        using var cgroup = new FakeCgroup().V2(cpuMax: "50000 100000");
        var cores = ContainerLimits.Read(cgroup.Root).CpuCores!.Value;
        var minute = TimeSpan.FromMinutes(1);
        var used = TimeSpan.FromSeconds(30);

        // When: the heartbeat measures CPU against the processor count, as it used to, and against the limit
        var againstProcessorCount = RuntimeHeartbeat.CpuPercent(used, minute, cores: 1);
        var againstLimit = RuntimeHeartbeat.CpuPercent(used, minute, cores);

        // Then: the old line said 50% and triage saw a queue with idle CPU; against the limit it's 100%, and
        // triage says the pod is CPU-bound (raise the limit or find the hot path), not starved of threads
        Assert.Equal((50, 100), ((int)againstProcessorCount, (int)againstLimit));
        HeartbeatSample Line(int m, double cpu) => new(
            new DateTimeOffset(2026, 10, 15, 9, m, 0, TimeSpan.Zero), Threads: 20, Queued: 300 * (m + 1), cpu,
            WorkingSetMb: 300, MemoryLimitMb: 1024, Gen2: 0, GcPauseMs: 50, LockContentions: 10, Exceptions: 5);
        Assert.Equal("threadpool.starvation", HeartbeatTriage.Assess([Line(0, againstProcessorCount), Line(1, againstProcessorCount)], cores: 1).Hypotheses[0].Name);
        Assert.Equal("cpu.bound", HeartbeatTriage.Assess([Line(0, againstLimit), Line(1, againstLimit)], cores: 1).Hypotheses[0].Name);
    }

    [Fact]
    public void Read_V2_TakesTheSmallestLimitBetweenTheContainerAndThePod()
    {
        using var cgroup = new FakeCgroup()
            .V2(ownGroup: "/kubepods.slice/pod1/app", cpuMax: "max 100000", memoryMax: "805306368")
            .Limits("/kubepods.slice/pod1", cpuMax: "150000 100000", memoryMax: "536870912");

        var limits = ContainerLimits.Read(cgroup.Root);

        Assert.Equal(new ContainerLimits(1.5, 512 * Mb), limits);
    }

    [Fact]
    public void Read_V2_NoLimits_ReturnsNulls()
    {
        using var cgroup = new FakeCgroup().V2(cpuMax: "max 100000", memoryMax: "max");

        Assert.Equal(new ContainerLimits(null, null), ContainerLimits.Read(cgroup.Root));
    }

    [Theory]
    [InlineData("25000", "100000", "268435456", 0.25, 256 * Mb)]
    [InlineData("-1", "100000", "9223372036854771712", null, null)]
    public void Read_V1_ReadsQuotaAndLimit_AndTreatsTheSentinelsAsNoLimit(string quota, string period, string memory, double? cores, long? bytes)
    {
        using var cgroup = new FakeCgroup().V1(quota, period, memory);

        Assert.Equal(new ContainerLimits(cores, bytes), ContainerLimits.Read(cgroup.Root));
    }

    [Fact]
    public void Read_NoCgroupFiles_ReturnsNulls()
    {
        using var cgroup = new FakeCgroup();

        Assert.Equal(new ContainerLimits(null, null), ContainerLimits.Read(cgroup.Root));
    }

    [Fact]
    public async Task Heartbeat_InAContainerWithLimits_LogsThemNextToTheHeapLimit()
    {
        using var cgroup = new FakeCgroup().V2(cpuMax: "50000 100000", memoryMax: "536870912");
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-15T00:00:00Z"));
        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.Configure<RuntimeHeartbeatOptions>(o => o.FileSystemRoot = cgroup.Root);
        builder.Services.AddHostedService<RuntimeHeartbeat>();
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            LogEntry? line = null;
            for (var i = 0; i < 100 && line is null; i++)
            {
                time.Advance(TimeSpan.FromMinutes(1));
                await Task.Delay(20);
                line = logs.Entries.FirstOrDefault(e => e.Message.StartsWith("Runtime heartbeat", StringComparison.Ordinal));
            }

            Assert.NotNull(line);
            Assert.Equal(0.5, Convert.ToDouble(line.Fields["CpuLimitCores"]));
            Assert.Equal(512, Convert.ToInt64(line.Fields["MemoryLimitMb"]));
            Assert.Equal(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / Mb, Convert.ToInt64(line.Fields["HeapLimitMb"]));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_TheRealCgroupFiles_ReadWithoutError_AndAgreeWithTheRuntime()
    {
        ProductionConditions.Require();

        var limits = ContainerLimits.Read();

        // On a runner there's usually no limit; in a pod there is. Either way the numbers have to make sense:
        // the runtime rounds a CPU limit up, so it never reports fewer processors than the limit allows.
        if (limits.CpuCores is { } cores) Assert.InRange(cores, 0.01, Environment.ProcessorCount);
        if (limits.MemoryBytes is { } bytes) Assert.True(bytes > 0);
    }
}
