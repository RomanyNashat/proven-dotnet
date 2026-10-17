using System.ComponentModel.DataAnnotations;

namespace ClinicReminders.Infrastructure;

public sealed class SmsOptions
{
    public const string Section = "Sms";

    // `required` only binds the compiler: when the setting is missing, the binder leaves this null.
    // [Required] is what catches it, at start-up (ValidateOnStart).
    [Required]
    public required Uri BaseAddress { get; init; }

    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
