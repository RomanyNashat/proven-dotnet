using System.ComponentModel.DataAnnotations;

namespace HomeReadings.Infrastructure;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    [Required]
    public string? PhotoRoot { get; set; }
}
