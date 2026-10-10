using System.ComponentModel.DataAnnotations;

namespace SkillSamples.Core;

public sealed class ShippingOptions
{
    public const string Section = "Shipping";

    // `required` only binds the compiler: when the setting is missing, the binder leaves this null.
    // [Required] is what catches it, at start-up (ValidateOnStart).
    [Required]
    public required Uri BaseAddress { get; init; }
}
