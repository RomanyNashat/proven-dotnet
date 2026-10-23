using System.ComponentModel.DataAnnotations;

namespace MeterSync.Infrastructure;

public sealed class SyncDatabaseOptions
{
    /// <summary>ConnectionStrings:Sync, with the pool capped (<see cref="SyncDatabase.WithPoolLimit"/>).</summary>
    [Required]
    public string? ConnectionString { get; set; }
}
