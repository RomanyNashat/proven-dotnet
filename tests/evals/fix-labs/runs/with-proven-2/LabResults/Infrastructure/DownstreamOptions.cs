using System.ComponentModel.DataAnnotations;

namespace LabResults.Infrastructure;

/// <summary>A downstream base address (<c>Labs:BaseAddress</c>, <c>Sms:BaseAddress</c>), checked at start-up.</summary>
public sealed class DownstreamOptions
{
    public const string Labs = "Labs";
    public const string Sms = "Sms";

    [Required]
    public Uri? BaseAddress { get; init; }

    /// <summary>
    /// The base address with a trailing '/', so a relative call keeps its path ("api/" + "patients/..."):
    /// without it HttpClient drops the last segment.
    /// </summary>
    public Uri NormalizedBaseAddress =>
        BaseAddress is { } uri && !uri.AbsoluteUri.EndsWith('/') ? new Uri(uri.AbsoluteUri + "/") : BaseAddress!;
}
