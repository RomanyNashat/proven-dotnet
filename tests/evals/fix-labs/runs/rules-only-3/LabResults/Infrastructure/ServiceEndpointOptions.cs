using System.ComponentModel.DataAnnotations;

namespace LabResults.Infrastructure;

/// <summary>Where an outside service lives (the <c>Labs</c> and <c>Sms</c> sections).</summary>
public sealed class ServiceEndpointOptions
{
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>The longest a single call may take; HttpClient's own default is 100 seconds.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
