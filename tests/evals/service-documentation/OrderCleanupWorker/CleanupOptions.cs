using System.ComponentModel.DataAnnotations;

namespace OrderCleanupWorker;

public sealed class CleanupOptions
{
    [Required] public string Schedule { get; init; } = "";
    [Range(30, 3650)] public int RetentionDays { get; init; }
    [Range(100, 10_000)] public int BatchSize { get; init; }
}
