using System.Globalization;

namespace SkillSamples.Diagnostics;

/// <summary>
/// The CPU and memory the container may use, read from its cgroup files. Null when there is no limit or
/// no cgroup (Windows, macOS, a process outside a container with no limit set).
/// </summary>
/// <remarks>
/// The runtime's own numbers aren't the container's: <see cref="Environment.ProcessorCount"/> rounds a
/// fractional CPU limit up (half a core reads as 1), and <see cref="GCMemoryInfo.TotalAvailableMemoryBytes"/>
/// is the GC heap limit, which in a container defaults to 75% of the memory limit.
/// </remarks>
public sealed record ContainerLimits(double? CpuCores, long? MemoryBytes)
{
    private const long NoLimitV1 = 1L << 62;   // cgroup v1 writes a page-rounded long.MaxValue for "no limit"

    /// <param name="root">The filesystem root; tests point it at a folder that holds fake cgroup files.</param>
    public static ContainerLimits Read(string root = "/")
    {
        var cgroup = Path.Combine(root, "sys", "fs", "cgroup");
        return File.Exists(Path.Combine(cgroup, "cgroup.controllers")) ? ReadV2(root, cgroup) : ReadV1(cgroup);
    }

    /// <summary>"max 100000" is no limit; "50000 100000" is half a core.</summary>
    public static double? ParseCpuMax(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts[0] != "max"
            ? double.Parse(parts[0], CultureInfo.InvariantCulture) / double.Parse(parts[1], CultureInfo.InvariantCulture)
            : null;
    }

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

    // cgroup v1, as a container sees it: its own group mounted at the controller's root.
    private static ContainerLimits ReadV1(string cgroup)
    {
        double? cpu = null;
        if (ReadText(Path.Combine(cgroup, "cpu", "cpu.cfs_quota_us")) is { } quota
            && ReadText(Path.Combine(cgroup, "cpu", "cpu.cfs_period_us")) is { } period
            && quota != "-1")
            cpu = double.Parse(quota, CultureInfo.InvariantCulture) / double.Parse(period, CultureInfo.InvariantCulture);

        long? memory = null;
        if (ReadText(Path.Combine(cgroup, "memory", "memory.limit_in_bytes")) is { } limit
            && long.Parse(limit, CultureInfo.InvariantCulture) is var bytes && bytes < NoLimitV1)
            memory = bytes;

        return new ContainerLimits(cpu, memory);
    }

    private static string? ReadText(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : null;
}
