using System.ComponentModel.DataAnnotations;

namespace MeterSync.Infrastructure;

/// <summary>A downstream HTTP service's settings, one named instance per configuration section.</summary>
public sealed class HttpServiceOptions
{
    public const string MeterVendor = "MeterVendor";
    public const string Sms = "Sms";

    // `required` binds only the compiler; [Required] is what stops a deploy that lost the setting.
    [Required]
    public Uri? BaseAddress { get; set; }
}
