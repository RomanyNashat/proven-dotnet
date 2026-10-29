using System.ComponentModel.DataAnnotations;

namespace LabResults.Infrastructure;

/// <summary>A downstream API's base address (<c>Labs:BaseAddress</c>, <c>Sms:BaseAddress</c>).</summary>
public sealed class BaseAddressOptions
{
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The address with a trailing '/'. Without it, <c>HttpClient</c> drops the last path segment
    /// ("…/api" + "patients/…" calls "…/patients/…").
    /// </summary>
    public Uri WithTrailingSlash() =>
        BaseAddress!.AbsoluteUri.EndsWith('/') ? BaseAddress : new Uri(BaseAddress.AbsoluteUri + "/");
}
