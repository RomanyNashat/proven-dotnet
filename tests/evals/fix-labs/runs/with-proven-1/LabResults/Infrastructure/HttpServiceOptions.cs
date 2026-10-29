using System.ComponentModel.DataAnnotations;

namespace LabResults.Infrastructure;

/// <summary>A downstream HTTP service: <c>Labs</c> or <c>Sms</c>. Validated at start-up.</summary>
public sealed class HttpServiceOptions
{
    public const string Labs = "Labs";
    public const string Sms = "Sms";

    // `required` binds only the compiler; [Required] is what catches a missing setting at start-up.
    [Required]
    public required Uri BaseAddress { get; init; }
}
