using System.ComponentModel.DataAnnotations;

namespace HomeReadings.Options;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    [Required(AllowEmptyStrings = false)]
    public string PhotoRoot { get; set; } = "";
}
