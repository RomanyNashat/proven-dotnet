using System.ComponentModel.DataAnnotations;

namespace MeterSync.Infrastructure;

public sealed class SmsOptions
{
    public const string Section = "Sms";

    [Required]
    public required Uri BaseAddress { get; init; }

    /// <summary>How long one POST to the gateway may take. It is never retried.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:00:15")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}
