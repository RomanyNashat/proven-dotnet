using System.ComponentModel.DataAnnotations;

namespace MeterSync.Infrastructure;

public sealed class MeterVendorOptions
{
    public const string Section = "MeterVendor";

    // `required` binds only the compiler; [Required] is what stops a deploy that lost the setting.
    [Required]
    public required Uri BaseAddress { get; init; }
}
